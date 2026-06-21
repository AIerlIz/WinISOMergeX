using System.Reflection;
using System.Security.Principal;
using WinISOMergeXCore.Interfaces;

namespace WinISOMergeXCore.Infrastructure.Services
{
    public class EnvironmentValidator : IEnvironmentValidator
    {
        private const string AppFolderName = "MergeTool";
        private const string ToolName = "oscdimg.exe";

        public bool IsAdministrator()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public string EnsureOscdimgReleased()
        {
            // 1. 定位到用户的 AppData\Local\MergeTool\Tools/
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string targetDir = Path.Combine(localAppData, AppFolderName, "Tools");
            string targetExePath = Path.Combine(targetDir, ToolName);

            // 2. 创建目录
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // 3. 幂等性校验：如果文件不存在，或者被阉割了（小于100KB，正常通常在100KB-200KB左右）
            if (!File.Exists(targetExePath) || new FileInfo(targetExePath).Length < 100 * 1024)
            {
                ReleaseEmbeddedAsset(targetExePath);
            }

            return targetExePath;
        }

        public long GetAvailableDiskSpace(string driveLetter)
        {
            try
            {
                var drive = new DriveInfo(driveLetter);
                return drive.AvailableFreeSpace;
            }
            catch
            {
                return 0; // 无法获取时防御性返回0
            }
        }

        private void ReleaseEmbeddedAsset(string targetPath)
        {
            var assembly = Assembly.GetExecutingAssembly();

            // 注意：这行的字符串必须匹配你项目中的 [默认命名空间.[文件夹].[文件名]]
            // 建议通过 assembly.GetManifestResourceNames() 打印出来核对
            string resourceName = "WinISOMergeXInfrastructure.Resources.oscdimg.exe";

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException($"在嵌入的资源中未找到组件: {resourceName}");
                }

                using (FileStream fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.CopyTo(fileStream);
                }
            }
        }
    }
}
