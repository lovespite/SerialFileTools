using System.Diagnostics;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;

namespace SerialFileTools;

public class FileInfo
{
    public string Name { get; set; } = null!;
    public long Length { get; set; }
    public byte[] Sha1 { get; set; } = null!;
}

public static class SerialPortHelper
{
    public static SerialPort Create(string portName, string? parameter = "9600,8,N,1")
    {
        parameter ??= "9600,8,N,1";

        var parts = parameter.Split(',');
        var baudRate = int.Parse(parts[0]);
        var dataBits = int.Parse(parts[1]);
        var parity = parts[2] switch
        {
            "N" => Parity.None,
            "E" => Parity.Even,
            "O" => Parity.Odd,
            "M" => Parity.Mark,
            "S" => Parity.Space,
            _ => throw new ArgumentException("Invalid parity.", nameof(parameter))
        };
        var stopBits = parts[3] switch
        {
            "1" => StopBits.One,
            "1.5" => StopBits.OnePointFive,
            "2" => StopBits.Two,
            _ => throw new ArgumentException("Invalid stop bits.", nameof(parameter))
        };
        return new SerialPort(portName, baudRate, parity, dataBits, stopBits);
    }

    private static void SendData(SerialPort sp, Memory<byte> data)
    {
        if (!sp.IsOpen)
        {
            sp.Open();
        }

        sp.Write(data.Span.ToArray(), 0, data.Length);
    }

    private static void SendData(SerialPort sp, string data)
    {
        if (!sp.IsOpen)
        {
            sp.Open();
        }

        sp.Write(data);
    }

    private static void SendFileInfo(SerialPort sp, string file)
    {
        var fileInfo = new System.IO.FileInfo(file);
        var name = fileInfo.Name;
        var size = fileInfo.Length;

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var sha1Bytes = SHA1.HashData(File.ReadAllBytes(file));
        var sizeBytes = BitConverter.GetBytes(size);

        var buffer = new byte[256];
        Array.Clear(buffer);
        buffer[0] = 0xAA;
        Array.Copy(sizeBytes, 0, buffer, 1, sizeBytes.Length);
        Array.Copy(sha1Bytes, 0, buffer, 9, sha1Bytes.Length);
        Array.Copy(nameBytes, 0, buffer, 29, nameBytes.Length);


        sp.Write(buffer, 0, 256);
        Console.WriteLine(Convert.ToHexString(buffer));
    }

    private static void SendFileData(SerialPort sp, string file, bool showDetail = false)
    {
        if (!sp.IsOpen)
        {
            sp.Open();
        }

        using var fs = File.OpenRead(file);
        var buffer = new byte[256];
        var read = 0;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            sp.Write(buffer, 0, read);
            if (showDetail) Console.WriteLine(Convert.ToHexString(buffer, 0, read));

            Task.Delay(50).Wait();

            if (sp.ReadByte() != 0xBB) throw new Exception("Error sending file.");
        }
    }

    private static FileInfo? ReceiveFileInfo(SerialPort sp)
    {
        if (!sp.IsOpen)
        {
            sp.Open();
        }

        while (sp.ReadByte() != 0xAA)
        {
        }

        var buffer = new byte[256];
        buffer[0] = 0xAA;
        var read = sp.Read(buffer, 1, buffer.Length - 1);
        Console.WriteLine(Convert.ToHexString(buffer));

        if (read == 0) return null;


        var sizeBytes = new byte[8];
        var sha1Bytes = new byte[20];
        var nameBytes = new byte[256 - 29];

        Array.Copy(buffer, 1, sizeBytes, 0, sizeBytes.Length);
        Array.Copy(buffer, 9, sha1Bytes, 0, sha1Bytes.Length);
        Array.Copy(buffer, 29, nameBytes, 0, nameBytes.Length);

        var size = BitConverter.ToInt64(sizeBytes);
        var sha1 = sha1Bytes;
        var name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

        return new FileInfo
        {
            Name = name,
            Length = size,
            Sha1 = sha1
        };
    }

    private static readonly byte[] FeedBack = { 0xBB };
    private static readonly byte[] StopFlag = { 0xFF };

    private static string ReceiveFileData(SerialPort sp, string dir, FileInfo file, bool showDetail = false)
    {
        if (!sp.IsOpen)
        {
            sp.Open();
        }

        var filePathname = Path.Combine(dir, file.Name);
        using var fs = File.Create(filePathname);
        var buffer = new byte[256];
        var read = 0;
        var totalRead = 0;

        while ((read = sp.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (showDetail) Console.WriteLine(Convert.ToHexString(buffer, 0, read));

            totalRead += read;

            fs.Write(buffer, 0, read);

            if (!showDetail)
            {
                // print progress
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"\r{totalRead}/{file.Length} {totalRead * 100 / file.Length}%");
            }
 
            sp.Write(FeedBack, 0, 1);

            Task.Delay(50).Wait();
            if (totalRead >= file.Length) break;
        }

        return filePathname;
    }


    public static void SendFile(SerialPort sp, string file, bool showDetail = false)
    {
        sp.DiscardOutBuffer();
        sp.DiscardInBuffer();
        
        SendFileInfo(sp, file);

        Task.Delay(50).Wait();

        var head = sp.ReadByte();

        if (head != 0xBB)
        {
            Console.WriteLine("Error sending file." + head.ToString("X"));
            return;
        }

        Task.Delay(50).Wait();
        SendFileData(sp, file, showDetail);
    }

    public static void ReceiveFile(SerialPort sp, string dir, bool showDetail = false)
    {
        var d = Directory.CreateDirectory(dir);
        var file = ReceiveFileInfo(sp);
        Task.Delay(50).Wait();
        if (file == null)
        {
            Console.WriteLine("Error receiving file.");
            sp.Write(StopFlag, 0, 1);
            return;
        }

        if (string.IsNullOrWhiteSpace(file.Name))
        {
            sp.Write(StopFlag, 0, 1);
            Console.WriteLine("Invalid file name.");
            return;
        }

        Console.WriteLine($"{file.Name} {file.Length} bytes {BitConverter.ToString(file.Sha1).Replace("-", "")}");

        Task.Delay(50).Wait();
        sp.DiscardInBuffer();
        
        sp.Write(FeedBack, 0, 1);
        Task.Delay(50).Wait();
        
        var f = ReceiveFileData(sp, dir, file, showDetail);
        var sha1 = SHA1.HashData(File.ReadAllBytes(f));
        if (!sha1.SequenceEqual(file.Sha1))
        {
            Console.WriteLine("Error receiving file. Sha1 mismatch.");
        }
        else
        {
            Console.WriteLine("\n\rFile received completely.");
        }
    }
}