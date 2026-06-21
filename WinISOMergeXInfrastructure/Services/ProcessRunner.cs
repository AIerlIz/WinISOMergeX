using System.Diagnostics;
using WinISOMergeXCore.Interfaces;

namespace WinISOMergeXCore.Infrastructure.Services
{
    public class ProcessRunner : IProcessRunner
    {
        public async Task<int> RunAsync(
            string exePath,
            string arguments,
            string workingDirectory,
            Action<string> onOutputReceived,
            Action<string> onErrorReceived,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,          // 必须为 false 才能重定向流
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,            // 隐藏黑窗口
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                // 异步读取控制台输出
                process.OutputDataReceived += (s, e) => { if (e.Data != null) onOutputReceived?.Invoke(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) onErrorReceived?.Invoke(e.Data); };

                process.Start();

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // 核心熔断联动：如果用户中途取消，立刻强杀外部进程
                using (cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                    catch { /* 忽略进程已经自然死亡的异常 */ }
                }))
                {
                    // 异步等待进程退出
                    await Task.Run(() => process.WaitForExit(), cancellationToken);
                    return process.ExitCode;
                }
            }
        }
    }
}
