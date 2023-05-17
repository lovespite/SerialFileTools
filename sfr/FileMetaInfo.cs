using System.Text; 

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
}