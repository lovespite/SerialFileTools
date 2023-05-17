namespace ControlledStreamProtocol.Extensions;

public static class StreamExtension
{
    public static ReadOnlyMemory<byte> ReadAllBytes(this Stream stream)
    {
        var ms = new MemoryStream();
        stream.Seek( 0, SeekOrigin.Begin);
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static void WriteAndFlush(this Stream stream, ReadOnlySpan<byte> data)
    {
        stream.Write(data);
        stream.Flush();
    } 
}