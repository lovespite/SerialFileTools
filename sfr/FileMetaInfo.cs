using System.Text;
using ControlledStreamProtocol;

namespace sfr;

public class FileMetaInfo
{
    public ushort BaseVersion { get; set; } = ProtocolBase.BaseVersion;
    public ushort ProtocolId { get; set; }
    public string FileName { get; set; } = null!;
    public long Length { get; set; }
    public byte[] Sha1 { get; set; } = null!;
    public int BlockSize { get; set; }

    public Meta AsMeta()
    {
        var meta = new Meta(
            Length,
            BlockSize,
            Sha1,
            ProtocolId,
            BaseVersion
        );
        meta.SetStringData(FileName, Encoding.UTF8);
        return meta;
    }

    public static class Builder
    {
        public static FileMetaInfo GetMetaInfo(ref Meta meta)
        {
            return new FileMetaInfo
            {
                FileName = meta.GetStringData().Trim(),
                Length = meta.Length,
                BlockSize = meta.BlockSize,
                Sha1 = meta.SignatureBlock.ToArray(),
                ProtocolId = meta.Protocol,
                BaseVersion = meta.BaseVersion
            };
        }
    }
}