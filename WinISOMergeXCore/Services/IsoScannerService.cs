using DiscUtils.Udf;
using System.Xml;
using WinISOMergeXCore.Models;

namespace WinISOMergeXCore.Services
{
    public class IsoScannerService
    {
        public async Task<List<WimImageIndexModel>> ScanIsoEntriesAsync(string isoPath, CancellationToken token)
        {
            return await Task.Run(() =>
            {
                var imageList = new List<WimImageIndexModel>();

                // 确保注册 UDF
                DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(UdfReader).Assembly);

                using (var isoStream = File.OpenRead(isoPath))
                using (var reader = new UdfReader(isoStream))
                {
                    string wimInIsoPath = null;
                    if (reader.FileExists(@"sources\install.wim")) wimInIsoPath = @"sources\install.wim";
                    else if (reader.FileExists(@"sources\install.esd")) wimInIsoPath = @"sources\install.esd";

                    if (string.IsNullOrEmpty(wimInIsoPath))
                    {
                        throw new FileNotFoundException($"该 ISO 镜像中未包含标准的 install.wim 或 install.esd 发行包。");
                    }

                    // 1. 获取 WIM 文件流
                    var fileInfo = reader.GetFileInfo(wimInIsoPath);
                    using (var wimStream = fileInfo.Open(FileMode.Open))
                    {
                        // 2. 高级技巧：定位并提取 WIM 尾部的 XML 描述块
                        // 为了绝对健壮性，如果不想手写 WIM Header 解析，可以直接提取开头的简易数据，
                        // 或者采用微软 DISM 临时释放 1MB 头信息。这里我们采用最稳健的解析：
                        XmlDocument xmlDoc = ReadWimXmlMetadata(wimStream);

                        if (xmlDoc == null) return imageList;

                        // 3. 解析 XML 节点
                        XmlNodeList imageNodes = xmlDoc.SelectNodes("//WIM/IMAGE");
                        foreach (XmlNode imageNode in imageNodes)
                        {
                            token.ThrowIfCancellationRequested();

                            int index = int.Parse(imageNode.Attributes["INDEX"]?.Value ?? "1");
                            string name = imageNode.SelectSingleNode("NAME")?.InnerText ?? "未知版本";
                            string arch = imageNode.SelectSingleNode("WINDOWS/ARCH")?.InnerText ?? "x64";
                            long size = long.Parse(imageNode.SelectSingleNode("TOTALBYTES")?.InnerText ?? "0");

                            // 转换架构显示 (将 9 转换为 x64，0 转换为 x86，微软原生规范)
                            if (arch == "9") arch = "x64";
                            else if (arch == "0") arch = "x86";
                            else if (arch == "12") arch = "Arm64";

                            imageList.Add(new WimImageIndexModel
                            {
                                IsSelected = true, // 默认让用户勾选
                                SourceIsoPath = isoPath,
                                ImageIndex = index,
                                ImageName = name,
                                Architecture = arch,
                                SizeInBytes = size
                            });
                        }
                    }
                }

                return imageList;
            }, token);
        }

        /// <summary>
        /// 从 WIM 核心流中快速定向抓取 XML 索引数据
        /// </summary>
        private XmlDocument ReadWimXmlMetadata(Stream wimStream)
        {
            try
            {
                byte[] header = new byte[72];
                wimStream.Read(header, 0, 72);

                // 验证 WIM 魔法头 "MSWIM\0\0\0"
                if (System.Text.Encoding.ASCII.GetString(header, 0, 8) != "MSWIM\0\0\0")
                    return null;

                // 从 Header 偏移量中读取 XML Data 的位置和大小
                long xmlOffset = BitConverter.ToInt64(header, 48);
                long xmlSize = BitConverter.ToInt64(header, 40); // 压缩或原始大小

                if (xmlOffset <= 0 || xmlSize <= 0) return null;

                // 移动指针到 XML 区块
                wimStream.Position = xmlOffset;
                byte[] xmlBytes = new byte[xmlSize];
                wimStream.Read(xmlBytes, 0, (int)xmlSize);

                // WIM 内部 XML 通常是小端 UTF-16 编码
                string xmlText = System.Text.Encoding.Unicode.GetString(xmlBytes);

                // 偶尔有前导垃圾字符，截取合法的 <WIM> 标签
                int startIndex = xmlText.IndexOf("<WIM>");
                if (startIndex >= 0)
                {
                    xmlText = xmlText.Substring(startIndex);
                }

                var doc = new XmlDocument();
                doc.LoadXml(xmlText);
                return doc;
            }
            catch
            {
                // 如果发生未知加密或压缩 WIM，防错返回
                return null;
            }
        }
    }
}
