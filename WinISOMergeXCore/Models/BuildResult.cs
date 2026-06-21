namespace WinISOMergeXCore.Models
{
    public class BuildResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> DetailLog { get; set; } // 纯内存运行日志缓存

        public BuildResult() => DetailLog = [];
    }
}
