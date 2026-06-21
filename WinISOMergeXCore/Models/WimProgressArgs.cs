namespace WinISOMergeXCore.Models
{
    public class WimProgressArgs
    {
        public ProgressStage Stage { get; set; }
        public string CurrentItem { get; set; }    // 当前正在处理的镜像名称
        public int Percentage { get; set; }         // 当前单步动作的百分比 (0-100)
        public string Message { get; set; }        // 详细的人性化提示文本
        public int CompletedTasks { get; set; }    // 队列已完成数
        public int TotalTasks { get; set; }        // 队列总任务数
    }
}
