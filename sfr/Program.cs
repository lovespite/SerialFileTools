global using ControlledStreamProtocol;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using ConsoleExtension;
using ControlledStreamProtocol.Extensions;
using ControlledStreamProtocol.PortStream;
using ControlledStreamProtocol.Static;
using sfr;

Logger.Low("SFR - Serial File Receiver v2.2.0");
Logger.Low("CopyRight (C) 2023, by Lovespite.");
Logger.Low("Protocol version: " + ProtocolBase.BaseVersion.ToString("X"));
Logger.Low("Platform: " + Environment.OSVersion);
Logger.Low("---------------------------------");
Logger.Low("Loading protocols...");

Protocol.LoadProtocolsFromPath(
    Path.Combine(
        Path.GetDirectoryName(AppContext.BaseDirectory) ?? Environment.CurrentDirectory,
        "Protocols"
    )
);

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
    var devs = UsbHelper.ListDevices(true);
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

Application.LoadArguments(args);

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
            Logger.Error(e.Message);
            return;
        }

        UsingReceivingMode();
    }
    else
    {
        Application.FileName = args.Skip(1).LastOrDefault(a => !a.StartsWith('-')) ?? string.Empty;
        UsingSendingMode();
    }
}

void HandleDebugReceiving(IControlledPortStream cps, Stream? fs)
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

    while (cps.IsOpen)
    {
        var bytes = new byte[dataBlockSize];
        try
        {
            var read = cps.Read(bytes);

            if (read <= 0) continue;

            totalReceived += read;

            if (fs is null)
            {
                Debug.Assert(ms is not null);
                ms.Write(bytes.AsSpan(0, read));

                Console.Clear();
                switch (Application.DebugView)
                {
                    case DebugViewMode.Hex:
                        PrintBytesData(ms.ToArray(), 16, true);
                        break;
                    case DebugViewMode.Text:
                        Console.Write(Application.TextEncoding.GetString(ms.ToArray()));
                        break;
                    case DebugViewMode.Both:
                    default:
                        PrintBytesData(ms.ToArray(), 16, true);
                        break;
                }

                if (ms.Length > maxDataBfSize) ms.SetLength(0);
            }
            else
            {
                fs.WriteAndFlush(bytes.AsSpan(0, read));

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
    using var cps = SerialPortHelper.Create(Application.PortName, Application.PortParameter);

    cps.Open();

    // ReSharper disable AccessToDisposedClosure
    Task.Run(() => HandleDebugReceiving(cps, fs));
    // ReSharper restore AccessToDisposedClosure

    try
    {
        string? input;
        while ((input = Console.ReadLine()) != "\0")
        {
            if (string.IsNullOrWhiteSpace(input)) continue;

            var data = GetSendingData(input).AsMemory();

            if (data.Length <= dataBlockSize)
            {
                cps.Write(data);
            }
            else
            {
                var total = 0;
                while (total < data.Length)
                {
                    var len = Math.Min(data.Length - total, dataBlockSize);
                    cps.Write(data[total..(total + len)]);
                    total += len;
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($" << {data.Length} bytes sent.\n");
            Console.ResetColor();
        }
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
    }
    finally
    {
        cps.Close();
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
    using var cps = SerialPortHelper.Create(Application.PortName, Application.PortParameter);
    PrintPortInfo(cps);

    Logger.Info("Receiving to \n - Directory: [" + Application.OutputDirectory + "]");

    while (true)
    {
        try
        {
            Logger.Info("Listening...");

            // receive file
            SerialPortHelper.Receive(cps);
        }
        catch (Exception e)
        {
            Console.WriteLine();
            Logger.Error($"\n{e.Message}");

#if DEBUG
            if (e.StackTrace is not null) Logger.Low(e.StackTrace);
#endif

            if (e.Message.Contains("port is closed")) break;
        }

        if (Application.KeepOpen) continue;
        break;
    }
}

void UsingSendingMode()
{
    using var cps = SerialPortHelper.Create(Application.PortName, Application.PortParameter);

    try
    {
        PrintPortInfo(cps);

        cps.Open();

        Logger.Info(">> Streaming... \n - File: [" + Path.GetFileName(Application.FileName) + "]");

        SerialPortHelper.SendFile(cps);
    }
    catch (Exception e)
    {
        Logger.Error($"\n{e.Message}");

#if DEBUG
        if (e.StackTrace is not null) Logger.Low(e.StackTrace);
#endif
    }
    finally
    {
        cps.Close();
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

void PrintPortInfo(IControlledPortStream serialPort)
{
    serialPort.PrintPortInfo();
}