namespace WinISOMergeXCore.Models
{
    public class WimImageIndexModel
    {
        public bool IsSelected { get; set; }       // UI 层的勾选状态
        public string SourceIsoPath { get; set; }   // 所属的源 ISO 路径
        public int ImageIndex { get; set; }        // 在源 WIM 中的原生索引编号 (如 1, 2, 3)
        public string ImageName { get; set; }      // 映像名称 (如 "Windows 11 专业版")
        public string Architecture { get; set; }   // 架构 (x64 / arm64)
        public long SizeInBytes { get; set; }      // 映像解压后的大小
    }
}
