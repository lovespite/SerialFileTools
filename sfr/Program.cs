using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using sfr;

CConsole.Low("SFR - Serial File Receiver v2.1.0");
CConsole.Low("CopyRight (C) 2023, by Lovespite.");
CConsole.Low("Protocol version: " + ProtocolBase.BaseVersion.ToString("X"));
CConsole.Low(Environment.OSVersion.ToString());
CConsole.Low("---------------------------------");


if (args.Length < 1 || args.Contains("--help") || args.Contains("-h"))
{
    Application.PrintUsage();
    return;
}

var modeListPorts = args.Contains("--list-ports") || args.Contains("-l");
if (modeListPorts)
{
    SerialPort.GetPortNames().ToList().ForEach(Console.WriteLine);
    return;
}

var modeListDevices = args.Contains("--list-devices") || args.Contains("-L");
if (modeListDevices)
{
    var devs = USBHelper.ListDevices(true);
    var index = 1;
    foreach (var dev in devs)
    {
        try
        {
            dev.Open(out var device);
            Console.WriteLine($"{index++} {dev.Name}");
            Console.WriteLine("  -      FullName: " + dev.FullName);
            Console.WriteLine("  -           Vid: " + dev.Vid.ToString("X"));
            Console.WriteLine("  -           Pid: " + dev.Pid.ToString("X"));
            Console.WriteLine("  -       Address: " + dev.DevicePath);
            Console.WriteLine("  -  Manufacturer: " + device.Info.ManufacturerString);
            Console.WriteLine("  -       Product: " + device.Info.ProductString);
            Console.WriteLine("  -     SerialNum: " + device.Info.SerialString);
            Console.WriteLine("  -    Descriptor: " + device.Info.Descriptor.Class);
            Console.WriteLine();
        }
        catch
        {
            // ignored
        }
    }

    return;
}

Application.KeepOpen = args.Contains("--keep-open") || args.Contains("-k");

Application.Debug = args.Contains("--debug") || args.Contains("-D");

Application.DebugView = Enum.Parse<DebugViewMode>((args.Where(a => a.StartsWith("--debug-view="))
    .Select(a => a[13..])
    .FirstOrDefault() ?? args.Where(a => a.StartsWith("-V="))
    .Select(a => a[3..])
    .FirstOrDefault()) ?? "both", true);

Application.TextEncodingStr = (args.Where(a => a.StartsWith("--text-encoding="))
    .Select(a => a[17..])
    .FirstOrDefault() ?? args.Where(a => a.StartsWith("-E="))
    .Select(a => a[3..])
    .FirstOrDefault()) ?? "us-ascii";


Application.Redirect = args.Where(a => a.StartsWith("--file="))
    .Select(a => a[7..])
    .FirstOrDefault() ?? args.Where(a => a.StartsWith("-f="))
    .Select(a => a[3..])
    .FirstOrDefault();


Application.Behavior = args.Contains("--send") || args.Contains("-s")
    ? PortMode.Send
    : args.Contains("--receive") || args.Contains("-r")
        ? PortMode.Receive
        : PortMode.Send;

Application.BlockSize = int.Parse((args
    .Where(a => a.StartsWith("--block-size="))
    .Select(a => a[13..])
    .FirstOrDefault() ?? args
    .Where(a => a.StartsWith("-b="))
    .Select(a => a[3..])
    .FirstOrDefault()) ?? "2048");

Application.PortParameter = (args.Where(a => a.StartsWith("--parameter="))
    .Select(a => a[12..])
    .FirstOrDefault() ?? args
    .Where(a => a.StartsWith("-p="))
    .Select(a => a[3..])
    .FirstOrDefault()) ?? Application.DefaultSerialPortParameter;

Application.PortName = args.First();

if (Application.Debug)
{
    UsingDebugMode();
}
else
{
    if (Application.Behavior == PortMode.Receive)
    {
        var dir = args.Skip(2).LastOrDefault(a => !a.StartsWith("-")) ?? Environment.CurrentDirectory;
        try
        {
            Application.OutputDirectory = Directory.CreateDirectory(dir).FullName;
        }
        catch (Exception e)
        {
            CConsole.Error(e.Message);
            return;
        }

        UsingReceivingMode();
    }
    else
    {
        Application.FileName = args.Skip(1).LastOrDefault() ?? string.Empty;
        UsingSendingMode();
    }
}

void HandleDebugReceiving(SerialPort serialPortInstance, Stream? fs)
{
    var dataBlockSize = SerialPortHelper.GetBlockSize();
    var maxDataBfSize = dataBlockSize * 12;
    var ms = fs is null ? new MemoryStream(maxDataBfSize) : null;

    if (fs is not null)
    {
        Console.WriteLine("Bytes data received redirecting to file: " + Application.Redirect);
    }
    else
    {
        Console.Clear();
    }

    var totalReceived = 0;

    while (serialPortInstance.IsOpen)
    {
        var bytes = new byte[dataBlockSize];
        try
        {
            var read = serialPortInstance.Read(bytes, 0, bytes.Length);
            if (read <= 0) continue;

            totalReceived += read;

            if (fs is null)
            {
                Debug.Assert(ms is not null);
                var data = new Span<byte>(bytes, 0, read);
                ms.Write(data);

                Console.Clear();
                switch (Application.DebugView)
                {
                    case DebugViewMode.Hex:
                        PrintBytesData(ms.ToArray());
                        break;
                    case DebugViewMode.Text:
                        Console.Write(Application.TextEncoding.GetString(ms.ToArray()));
                        break;
                    default:
                        PrintBytesData(ms.ToArray(), 16, true);
                        break;
                }

                if (ms.Length > maxDataBfSize) ms.SetLength(0);
            }
            else
            {
                fs.WriteAndFlush(bytes, 0, read);

                // Console.SetCursorPosition(0, Console.CursorTop);
                // Console.Write(">> Bytes data received: " + totalReceived);

                Console.WriteLine(
                    $"{DateTime.Now:HH:mm:ss.fff} >> Bytes Received: {read,4}, total: {totalReceived,8}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }
}

void UsingDebugMode()
{
    var dataBlockSize = SerialPortHelper.GetBlockSize();
    using Stream? fs = Application.Redirect is null ? null : File.Create(Application.Redirect);
    using var serialPortInstance = SerialPortHelper.Create(Application.PortName, Application.PortParameter);

    serialPortInstance.Open();

    // ReSharper disable AccessToDisposedClosure
    using var t = Task.Run(() => HandleDebugReceiving(serialPortInstance, fs));
    // ReSharper restore AccessToDisposedClosure

    try
    {
        string? input;
        while ((input = Console.ReadLine()) != "\0")
        {
            if (string.IsNullOrWhiteSpace(input)) continue;

            var data = GetSendingData(input);
            if (data.Length <= dataBlockSize)
            {
                serialPortInstance.Write(data, 0, data.Length);
            }
            else
            {
                var offset = 0;
                while (offset < data.Length)
                {
                    var length = Math.Min(dataBlockSize, data.Length - offset);
                    serialPortInstance.Write(data, offset, length);
                    offset += length;
                }
            }

            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} << {data.Length} bytes sent.");
        }
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
    }
    finally
    {
        serialPortInstance.Close();
    }

    byte[] GetSendingData(string raw)
    {
        return Application.DebugView == DebugViewMode.Hex
            ? Convert.FromHexString(raw)
            : Application.TextEncoding.GetBytes(raw);
    }
}

void UsingReceivingMode()
{
    using var serialPortInstance = SerialPortHelper.Create(Application.PortName, Application.PortParameter);

    PrintPortInfo(serialPortInstance);

    CConsole.Info("Receiving to \n - Directory: [" + Application.OutputDirectory + "]");

    serialPortInstance.Open();
    while (true)
    {
        try
        {
            CConsole.Info("Listening...");

            // clear buffer
            serialPortInstance.DiscardInBuffer();
            serialPortInstance.DiscardOutBuffer();

            // receive file
            SerialPortHelper.Receive(serialPortInstance);
        }
        catch (Exception e)
        {
            CConsole.Error(e.Message);
            if(e.StackTrace is not null) CConsole.Low(e.StackTrace);
        }

        if (Application.KeepOpen) continue;
        break;
    }
}

void UsingSendingMode()
{
    try
    {
        using var serialPortInstance = SerialPortHelper.Create(Application.PortName, Application.PortParameter);

        PrintPortInfo(serialPortInstance);

        CConsole.Info("Sending: \n - File: [" + Path.GetFileName(Application.FileName) + "]");

        serialPortInstance.Open();

        // clear buffer
        serialPortInstance.DiscardInBuffer();
        serialPortInstance.DiscardOutBuffer();

        SerialPortHelper.SendFile(serialPortInstance);

        serialPortInstance.Close();
    }
    catch (Exception e)
    {
        CConsole.Error(e.Message);
        if (e.StackTrace is not null) CConsole.Low(e.StackTrace);
    }
}

void PrintBytesData(byte[] data, int maxWidth = 16, bool withText = false)
{
    var offset = 0;
    while (offset < data.Length)
    {
        var length = Math.Min(maxWidth, data.Length - offset);
        var line = data[offset..(offset + length)];
        var lineHexStr = $"{string.Join(" ", line.Select(b => b.ToString("X2"))).PadRight(maxWidth * 3)}";

        if (withText)
        {
            // replace not printable char
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] < 0x20 || line[i] > 0x7e) line[i] = 0x2e;
            }

            lineHexStr += $" ┃ {Encoding.ASCII.GetString(line)}";
        }

        Console.WriteLine(lineHexStr);
        offset += length;
    }
}

void PrintPortInfo(SerialPort serialPort)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("Using port [{0}] ({1}, {2}, {3}, {4}), Block Size: {5}",
        serialPort.PortName,
        serialPort.BaudRate,
        serialPort.DataBits,
        serialPort.Parity,
        serialPort.StopBits,
        SerialPortHelper.GetBlockSize());
    Console.ResetColor();
}