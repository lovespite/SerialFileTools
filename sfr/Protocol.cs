namespace sfr;

public enum ByteFlag
{
    Continue = 0xBB,
    StopBy = 0xFF,
    Incomplete = 0xF8,
    ProtocolMismatch = 0xF7,
}
public class Protocol
{
    public const ushort ProtocolVersion = 0x1000;
}