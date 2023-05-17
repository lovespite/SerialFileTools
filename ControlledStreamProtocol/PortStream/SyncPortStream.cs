using System.IO.Ports;
using ControlledStreamProtocol.Exceptions;

namespace ControlledStreamProtocol.PortStream;

public class SyncPortStream : IControlledPortStream
{
    private readonly SerialPort _port;

    public bool IsOpen => _port.IsOpen;

    // ReSharper disable once MemberCanBePrivate.Global
    public SyncPortStream(SerialPort port)
    {
        _port = port;
    }

    private const int BaseBaudRate = 115200;

    public static SyncPortStream Create(string portName, string parameter)
    {
        var parts = parameter.Split(',');
        var baudRate = parts[0] switch
        {
            "1x" => BaseBaudRate,
            "2x" => BaseBaudRate * 2,
            "4x" => BaseBaudRate * 4,
            _ => int.Parse(parts[0]),
        };

        var dataBits = parts.Length > 1 ? int.Parse(parts[1]) : 8;

        var parity = parts.Length > 2
            ? parts[2] switch
            {
                "N" => Parity.None,
                "E" => Parity.Even,
                "O" => Parity.Odd,
                "M" => Parity.Mark,
                "S" => Parity.Space,
                _ => throw new ArgumentException("Invalid parity.", nameof(parameter))
            }
            : Parity.None;

        var stopBits = parts.Length > 3
            ? parts[3] switch
            {
                "1" => StopBits.One,
                "1.5" => StopBits.OnePointFive,
                "2" => StopBits.Two,
                _ => throw new ArgumentException("Invalid stop bits.", nameof(parameter))
            }
            : StopBits.One;

        // ReSharper disable StringLiteralTypo
        var handShake = parts.Length > 4
            ? parts[4].ToLower() switch
            {
                "none" or "n" => Handshake.None,
                "xonxoff" or "x" => Handshake.XOnXOff,
                "requesttosend" or "rts" => Handshake.RequestToSend,
                "RequestToSendXOnXOff" or "rtsxx" => Handshake.RequestToSendXOnXOff,
                _ => throw new ArgumentException("Invalid hand shake.", nameof(parameter))
            }
            : Handshake.None;
        // ReSharper restore StringLiteralTypo

        var sp = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
        {
            Handshake = handShake,
        };

        return new SyncPortStream(sp);
    }

    public void Dispose()
    {
        _port.Dispose();
        GC.SuppressFinalize(this);
    }

    public byte ReadByte()
    {
        var read = _port.ReadByte();
        return read switch
        {
            >= 0 => (byte)read,
            -1 => throw new StreamReachingEndException(),
            _ => throw new StreamReadFailedException()
        };
    }

    public void Open() => _port.Open();

    public void Close() => _port.Close();

    public void PrintPortInfo()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Using port [{0}] ({1}, {2}, {3}, {4}, {5})",
            _port.PortName,
            _port.BaudRate,
            _port.DataBits,
            _port.Parity,
            _port.StopBits,
            _port.Handshake
        );
        Console.ResetColor();
    }

    public int ReadTimeout
    {
        get => _port.ReadTimeout;
        set => _port.ReadTimeout = value;
    }

    public void DiscardInBuffer()
    {
        if (_port.IsOpen) _port.DiscardInBuffer();
    }

    public void DiscardOutBuffer()
    {
        if (_port.IsOpen) _port.DiscardOutBuffer();
    }


    public void Write(ReadOnlyMemory<byte> buffer)
    {
        _port.Write(buffer.ToArray(), 0, buffer.Length);
    }

    public int ReadAtLeast(byte[] buffer, int offset, int count, int msTimeout = 1000)
    {
        var start = DateTime.Now;
        var read = 0;
        var span = TimeSpan.FromMilliseconds(msTimeout);

        _port.ReadTimeout = msTimeout;
        try
        {
            while (read < count)
            {
                if (DateTime.Now - start > span) break;

                read += _port.Read(buffer, offset + read, count - read);
                start = DateTime.Now;
                if (read == 0) Task.Delay(10).Wait();
            }
        }
        catch (TimeoutException)
        {
            // ignored
        }
        finally
        {
            _port.ReadTimeout = SerialPort.InfiniteTimeout;
        }

        return read;
    }

    public int ReadAtLeast(byte[] buffer, int msTimeout = 1000)
    {
        return ReadAtLeast(buffer, 0, buffer.Length);
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        return _port.Read(buffer, offset, count);
    }

    public int Read(byte[] buffer)
    {
        return _port.Read(buffer, 0, buffer.Length);
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        _port.Write(buffer, offset, count);
    }
}