using DiscUtils.Udf;
using System.Collections.Concurrent;
using WinISOMergeXCore.Interfaces;
using WinISOMergeXCore.Models;

namespace WinISOMergeXCore.Services
{
    public class WorkflowManager
    {
        private readonly IProcessRunner _processRunner;
        private readonly IEnvironmentValidator _validator;
        private readonly List<string> _logCache;

        public WorkflowManager(IProcessRunner processRunner, IEnvironmentValidator validator)
        {
            _processRunner = processRunner;
            _validator = validator;
            _logCache = [];
        }

        public async Task<BuildResult> StartMergeWorkflowAsync(
            List<WimImageIndexModel> selectedTasks,
            string skeletonIsoPath,
            string targetIsoPath,
            string volumeLabel,
            IProgress<WimProgressArgs> progress,
            CancellationToken cancellationToken)
        {
            var result = new BuildResult();
            _logCache.Clear();

            // 创建唯一的工作沙箱目录
            string tempWorkspace = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Workspace_" + Guid.NewGuid().ToString("N"));
            string tempWimDir = Path.Combine(tempWorkspace, "sources");
            string targetWimPath = Path.Combine(tempWimDir, "install.wim");

            Log("======== 启动大合一压制流水线 ========");

            try
            {
                // 1. 预检与资产释放
                ReportProgress(progress, ProgressStage.Validating, "准备中", 0, "正在校验系统环境与释放组件...");
                if (!_validator.IsAdministrator())
                {
                    throw new UnauthorizedAccessException("权限不足：必须以管理员身份运行此程序。");
                }
                string oscdimgPath = _validator.EnsureOscdimgReleased();
                Log($"[基础层] 资产校验成功。Oscdimg 路径: {oscdimgPath}");

                // 创建沙箱
                Directory.CreateDirectory(tempWimDir);

                // 2. 释放引导骨架 (全盘复制，精准拉黑 install.wim)
                ReportProgress(progress, ProgressStage.Extracting, "骨架 ISO", 10, "正在提取引导骨架(排除大文件)...");
                Log($"[DiscUtils] 开始解压骨架 ISO: {skeletonIsoPath}");

                // 【此处调用 DiscUtils 逻辑】
                await ExtractSkeletonAsync(skeletonIsoPath, tempWorkspace, cancellationToken);
                Log("[DiscUtils] 骨架提取完成，已剔除原始 install.wim");

                // 3. 构建单线程串行队列
                var taskQueue = new ConcurrentQueue<WimImageIndexModel>(selectedTasks);
                int totalTasks = selectedTasks.Count;
                int completedTasks = 0;

                Log($"[队列系统] 成功加载串行任务，共计 {totalTasks} 个映像需要合并。");

                // 4. 串行循环消费队列
                while (taskQueue.TryDequeue(out var currentTask))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    completedTasks++;
                    string taskInfo = $"{currentTask.ImageName} (来自 ISO [{Path.GetFileName(currentTask.SourceIsoPath)}])";
                    Log($"[串行开始] 正在处理 ({completedTasks}/{totalTasks}): {taskInfo}");

                    // 4.1 从当前任务的 ISO 里单独提取其 install.wim 到零时单体文件
                    string singleTempWim = Path.Combine(tempWorkspace, $"temp_{completedTasks}.wim");

                    ReportProgress(progress, ProgressStage.Merging, currentTask.ImageName, 20,
                        $"正在解析源文件... ({completedTasks}/{totalTasks})", completedTasks, totalTasks);

                    // 【此处调用 DiscUtils 提取单个 install.wim】
                    await ExtractSingleWimAsync(currentTask.SourceIsoPath, singleTempWim, cancellationToken);

                    // 4.2 调用 DISM API 追加导出到目标核心 install.wim
                    ReportProgress(progress, ProgressStage.Merging, currentTask.ImageName, 50,
                        $"正在执行 DISM 映像追加... ({completedTasks}/{totalTasks})", completedTasks, totalTasks);

                    // 【此处调用 Microsoft.Dism 托管驱动库】
                    // 模拟：DismApi.ExportImage(singleTempWim, currentTask.ImageIndex, targetWimPath, ...);
                    await ExecuteDismExportAsync(singleTempWim, currentTask.ImageIndex, targetWimPath, cancellationToken);
                    Log($"[DISM 成功] 映像已安全追加到主 WIM。");

                    // 4.3 【关键点：空间极致回收】用完立刻粉碎单体临时 WIM
                    ReportProgress(progress, ProgressStage.Merging, currentTask.ImageName, 90,
                        $"正在释放零时缓存空间... ({completedTasks}/{totalTasks})", completedTasks, totalTasks);

                    if (File.Exists(singleTempWim))
                    {
                        File.Delete(singleTempWim);
                        Log($"[内存回收] 已成功粉碎单体临时文件: {Path.GetFileName(singleTempWim)}");
                    }
                }

                // 5. 终局压制 (调用外部二进制 oscdimg.exe)
                ReportProgress(progress, ProgressStage.Packaging, "打包 ISO", 10, "核心 WIM 合并已就绪，正在通过 OSCDIMG 炼制最终引导镜像...");
                Log("[OSCDIMG] 开始构建标准可引导光盘映像...");

                // 组装无窗口命令行参数：启用长文件名、带有 UDF 引导、注入卷标
                string oscdimgArgs = $"-m -o -u2 -udfver102 -l\"{volumeLabel}\" -bootdata:2#p0,e,b\"{Path.Combine(tempWorkspace, "boot", "etfsboot.com")}\"#pEF,e,b\"{Path.Combine(tempWorkspace, "efi", "microsoft", "boot", "efisys.bin")}\" \"{tempWorkspace}\" \"{targetIsoPath}\"";

                int exitCode = await _processRunner.RunAsync(
                    oscdimgPath,
                    oscdimgArgs,
                    tempWorkspace,
                    stdout => Log($"[OSCDIMG 输出] {stdout}"),
                    stderr => Log($"[OSCDIMG 警告] {stderr}"),
                    cancellationToken
                );

                if (exitCode != 0)
                {
                    throw new Exception($"OSCDIMG 压制失败，外部程序退出码: {exitCode}");
                }
                Log($"[成功] 最终引导 ISO 已成功输出至: {targetIsoPath}");

                // 6. 成功收尾清理
                ReportProgress(progress, ProgressStage.Finalizing, "清理战场", 95, "完成！正在擦除工作沙箱...");
                CleanWorkspace(tempWorkspace);

                ReportProgress(progress, ProgressStage.Completed, "全部完成", 100, "多合一 ISO 镜像制作成功！");
                result.IsSuccess = true;
            }
            catch (OperationCanceledException)
            {
                Log("[用户熔断] 用户点击了取消按钮，触发紧急终止程序。");
                HandleFailureCleanup(tempWorkspace, progress);
                result.IsSuccess = false;
                result.ErrorMessage = "操作已被用户取消。";
            }
            catch (Exception ex)
            {
                Log($"[致命错误] 流水线崩溃！原因: {ex.Message}\n{ex.StackTrace}");
                HandleFailureCleanup(tempWorkspace, progress);
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                result.DetailLog.AddRange(_logCache);
            }

            return result;
        }

        #region 私有辅助技术落地方法 (Mock/占位，后续填入具体第三方API)

        private void Log(string text)
        {
            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}";
            _logCache.Add(logLine);
        }

        private void ReportProgress(IProgress<WimProgressArgs> progress, ProgressStage stage, string item, int pct, string msg, int comp = 0, int total = 0)
        {
            progress?.Report(new WimProgressArgs
            {
                Stage = stage,
                CurrentItem = item,
                Percentage = pct,
                Message = msg,
                CompletedTasks = comp,
                TotalTasks = total
            });
        }

        private void HandleFailureCleanup(string workspace, IProgress<WimProgressArgs> progress)
        {
            ReportProgress(progress, ProgressStage.Finalizing, "紧急避险", 50, "流水线已断开！正在对系统盘执行强一致性垃圾粉碎...");
            CleanWorkspace(workspace);
            ReportProgress(progress, ProgressStage.Failed, "已中止", 0, "战场已还原，无任何残余碎片占用空间。");
        }

        private void CleanWorkspace(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                    Log($"[强一致性清理] 沙箱工作目录已被连根拔除: {path}");
                }
            }
            catch (Exception ex)
            {
                Log($"[清理警告] 无法完全擦除沙箱目录，部分文件可能被独占: {ex.Message}");
            }
        }

        private async Task ExtractSkeletonAsync(string isoPath, string destDir, CancellationToken token)
        {
            // 必须注册 UDF 编解码器（DiscUtils 要求的初始化）
            DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(UdfReader).Assembly);

            await Task.Run(() =>
            {
                using (var isoStream = File.OpenRead(isoPath))
                using (var reader = new UdfReader(isoStream))
                {
                    // 递归遍历 ISO 内的所有文件和文件夹
                    foreach (var file in reader.GetFiles("", "*.*", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();

                        // 【核心防呆】精准拉黑主映像文件，不管它是 .wim 还是 .esd
                        if (file.EndsWith(@"sources\install.wim", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(@"sources\install.esd", StringComparison.OrdinalIgnoreCase))
                        {
                            Log($"[骨架提取] 已拦截并跳过大文件: {file}");
                            continue;
                        }

                        // 计算本地释放的绝对路径
                        string targetPath = Path.Combine(destDir, file);
                        string targetFolder = Path.GetDirectoryName(targetPath);

                        if (!Directory.Exists(targetFolder))
                        {
                            Directory.CreateDirectory(targetFolder);
                        }

                        // 流式写入沙箱
                        var srcFileInfo = reader.GetFileInfo(file);
                        using (var srcStream = srcFileInfo.Open(FileMode.Open))
                        using (var destStream = File.Create(targetPath))
                        {
                            srcStream.CopyTo(destStream);
                        }
                    }
                }
            }, token);
        }

        private async Task ExtractSingleWimAsync(string iso, string destWim, CancellationToken token)
        {
            // 此处后续搬砖：利用 DiscUtils 单独把 \sources\install.wim 丢出来
            await Task.Delay(500, token); // 模拟耗时
        }

        private async Task ExecuteDismExportAsync(string srcWim, int idx, string destWim, CancellationToken token)
        {
            // 此处后续搬砖：调用 Managed DismApi 执行合并
            await Task.Delay(500, token); // 模拟耗时
        }

        #endregion
    }
}
