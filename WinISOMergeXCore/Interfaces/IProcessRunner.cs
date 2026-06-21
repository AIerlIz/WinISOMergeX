namespace WinISOMergeXCore.Interfaces
{
    // 外部可执行程序(oscdimg)的编排接口
    public interface IProcessRunner
    {
        Task<int> RunAsync(
            string exePath,
            string arguments,
            string workingDirectory,
            Action<string> onOutputReceived,
            Action<string> onErrorReceived,
            CancellationToken cancellationToken);
    }

    // 环境预检接口
    public interface IEnvironmentValidator
    {
        bool IsAdministrator();
        string EnsureOscdimgReleased(); // 返回释放后的绝对路径
        long GetAvailableDiskSpace(string driveLetter);
    }
}
