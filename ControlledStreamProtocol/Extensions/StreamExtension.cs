using System.IO.Ports;

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

    public static void WriteAndFlush(this Stream stream, byte[] data)
    {
        stream.Write(data);
        stream.Flush();
    }

    public static void WriteAndFlush(this Stream stream, byte[] data, int offset, int count)
    {
        stream.Write(data, offset, count);
        stream.Flush();
    }

    /// <summary>
    /// Read until the specified byte is received.
    /// </summary>
    /// <param name="serialPort"></param>
    /// <param name="buffer"></param>
    /// <param name="offset"></param>
    /// <param name="count"></param>
    /// <param name="timeout"></param>
    /// <returns></returns>
    public static int ReadAtLeast(this SerialPort serialPort, byte[] buffer, int offset, int count, int timeout = 500)
    {
        var start = DateTime.Now;
        var read = 0;
        serialPort.ReadTimeout = timeout;
        var span = TimeSpan.FromMilliseconds(timeout);
        
        while (read < count)
        {
            if (DateTime.Now - start > span) break;
            try
            {
                read += serialPort.Read(buffer, offset + read, count - read);
                start = DateTime.Now;
                if (read == 0) Task.Delay(10).Wait();
            }
            catch (TimeoutException)
            {
                break;
            } 
        }

        serialPort.ReadTimeout = SerialPort.InfiniteTimeout;
        return read;
    }
}