namespace ControlledStreamProtocol;

public enum ByteFlag
{
    Head = 0xAA,
    Continue = 0xBB,
    StopBy = 0xFF,
    Incomplete = 0xF8,
    ProtocolMismatch = 0xF7,
    ProtocolNotSupported = 0xF6,
}

public static class Flag
{  
    public static string GetErrorMessage(byte signal)
    {
        return signal switch
        {
            (byte)ByteFlag.Continue => "Continue.",
            (byte)ByteFlag.StopBy => "Stopped by the other side.",
            (byte)ByteFlag.Incomplete => "Incomplete data block.",
            (byte)ByteFlag.ProtocolMismatch => "Protocol mismatch. The other side is using a different protocol.",
            (byte)ByteFlag.ProtocolNotSupported => "Protocol not supported.",
            _ => "Unknown error."
        };
    }
    
    public static string GetErrorMessage(ByteFlag signal)
    {
        return GetErrorMessage((byte)signal);
    }
}