namespace ControlledStreamProtocol.PortStream;

public interface IControlledPortStream: IDisposable
{
    public byte ReadByte();
    
    // 
    public bool IsOpen { get; }
    public void Open();
    public void Close();
    
    public void PrintPortInfo();
    
    public int ReadTimeout { get; set; }
    
    public void DiscardInBuffer();
    public void DiscardOutBuffer();
    
    public const int InfiniteTimeout = -1;
    
    public int ReadAtLeast(byte[] buffer, int offset, int count, int msTimeout = 1000);
    public int ReadAtLeast(byte[] buffer, int msTimeout = 1000);
    public int Read(byte[] buffer, int offset, int count);
    public int Read(byte[] buffer);
    
    // high level
    public void Write(ReadOnlyMemory<byte> buffer);
    
    // low level
    public void Write(byte[] buffer, int offset, int count); 
}