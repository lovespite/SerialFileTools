global using ControlledStreamProtocol;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using ConsoleExtension;
using ControlledStreamProtocol.Extensions;
using ControlledStreamProtocol.PortStream;
using ControlledStreamProtocol.Static;
using sfr;

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

Logger.Low("SFR - Serial File Receiver v2.5.1");
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

Application.LoadArguments(args);

if (Application.Debug)
{
    UsingDebugMode();
    return;
}

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
        Logger.Low(e.StackTrace ?? string.Empty);
        return;
    }

    UsingReceivingMode();
}
else
{
    Application.FileName = args.Skip(1).LastOrDefault(a => !a.StartsWith('-')) ?? string.Empty;
    UsingSendingMode();
}

void HandleDebugReceiving(IControlledPortStream cps, Stream? fs)
{
    var dataBlockSize = Application.BlockSize;
    var maxDataBfSize = (long)dataBlockSize * 128;
    var ms = fs is null ? new MemoryStream(dataBlockSize * 16) : null;

    if (ms is null)
    {
        // output to file
        Logger.Info("Bytes data received redirecting to file: " + Application.Redirect);
    }
    else
    {
        Console.Clear();
    }

    var totalReceived = 0;

    var sw = Stopwatch.StartNew();

    if (ms is not null) Task.Run(() =>
    {
        while (true)
        {
            if (sw.ElapsedMilliseconds < 490)
            {
                Task.Delay(10).Wait();
                continue;
            }

            Render(ms);
            sw.Stop();
            sw.Reset();
        }
    });

    while (cps.IsOpen)
    {
        var bytes = new byte[dataBlockSize];
        try
        {
            var read = cps.Read(bytes);

            if (read <= 0)
            {
                Task.Delay(1000).Wait();
                Logger.Low(">> No data received.");
                continue;
            }

            totalReceived += read;

            if (fs is null)
            {
                if (ms!.Length > maxDataBfSize) ms.SetLength(0);
                ms!.Write(bytes.AsSpan(0, read));
                sw.Restart();
            }
            else
            {
                fs.WriteAndFlush(bytes.AsSpan(0, read));

                // Console.SetCursorPosition(0, Console.CursorTop);
                // Console.Write(">> Bytes data received: " + totalReceived);

                Logger.Info(
                    $"{DateTime.Now:HH:mm:ss.fff} >> Bytes Received: {read,4}, total: {totalReceived,8}");
            }
        }
        catch (Exception e)
        {
            Logger.Error($"\n{e.Message}");
            Logger.Low(e.StackTrace ?? string.Empty);
        }
    }

    ms?.Dispose();
    ms = null;

    void Render(MemoryStream ms)
    {
        Console.Clear();
        switch (Application.DebugView)
        {
            case DebugViewMode.Hex:
                PrintBytesData(ms.ToArray(), 16, true);
                break;
            case DebugViewMode.Text:
                Console.WriteLine(Application.TextEncoding.GetString(ms.ToArray()));
                break;
            case DebugViewMode.Both:
            default:
                PrintBytesData(ms.ToArray(), 16, true);
                break;
        }
        Logger.Low("\n>> Bytes data received: " + totalReceived);
    }
}

void UsingDebugMode()
{
    var dataBlockSize = Application.BlockSize;
    using Stream? fs = Application.Redirect is null ? null : File.Open(Application.Redirect, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
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

            if (Application.DebugView == DebugViewMode.Text) input += "\r\n";

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

            Logger.Ok(">> Sent: " + data.Length);
        }
    }
    catch (Exception e)
    {
        Logger.Error($"\n{e.Message}");
        Logger.Low(e.StackTrace ?? string.Empty);
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
        catch (InvalidOperationException)
        {
            // port closed
            Logger.Warn(">> Port closed.");
            break;
        }
        catch (Exception e)
        {
            Console.WriteLine();
            Logger.Warn(">> " + e.GetType().Name);
            Logger.Error($"\n{e.Message}");
            Logger.Low(e.StackTrace ?? string.Empty);
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
        Console.WriteLine();
        Logger.Warn(">> " + e.GetType().Name);
        Logger.Error($"\n{e.Message}");
        Logger.Low(e.StackTrace ?? string.Empty);
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