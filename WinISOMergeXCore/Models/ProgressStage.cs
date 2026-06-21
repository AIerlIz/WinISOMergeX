namespace WinISOMergeXCore.Models
{
    public enum ProgressStage
    {
        Validating,   // 环境预检中
        Extracting,   // 正在提取骨架或文件
        Merging,      // 正在串行合并 WIM 索引
        Packaging,    // 正在调用 OSCDIMG 压制 ISO
        Finalizing,   // 强一致性战场清理中
        Completed,    // 完美收工
        Failed        // 发生致命熔断
    }
}
