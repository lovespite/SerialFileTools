using System.Diagnostics;
using System.IO.Ports;
using System.Reflection;
using System.Text;
using sfr;

string[] allowedEncodings = { "us-ascii", "utf-8", "utf-16", "utf-32" };

if (args.Length < 1 || args.Contains("--help") || args.Contains("-h"))
{
    PrintUsage();
    return;
}

var modeListPorts = args.Contains("--list-ports") || args.Contains("-l");
if (modeListPorts)
{
    SerialPort.GetPortNames().ToList().ForEach(Console.WriteLine);
    return;
}

var modeShowDetail = args.Contains("--detail") || args.Contains("-d");
var modeKeepOpen = args.Contains("--keep-open") || args.Contains("-k");

var modeDebug = args.Contains("--debug") || args.Contains("-D");
var modeDebugView = (args.Where(a => a.StartsWith("--debug-view="))
    .Select(a => a[13..])
    .FirstOrDefault() ?? args.Where(a => a.StartsWith("-V="))
    .Select(a => a[3..])
    .FirstOrDefault()) ?? "both";
var textEncodingString = (args.Where(a => a.StartsWith("--text-encoding="))
    .Select(a => a[17..])
    .FirstOrDefault() ?? args.Where(a => a.StartsWith("-E="))
    .Select(a => a[3..])
    .FirstOrDefault()) ?? "us-ascii";
var fileRedirect = args.Where(a => a.StartsWith("--file="))
    .Select(a => a[7..])
    .FirstOrDefault() ?? args.Where(a => a.StartsWith("-f="))
    .Select(a => a[3..])
    .FirstOrDefault();

if (!allowedEncodings.Contains(textEncodingString))
{
    Console.WriteLine($"Invalid text encoding: {textEncodingString}");
    Console.WriteLine("Allowed encodings: " + string.Join(", ", allowedEncodings));
    return;
}

// ReSharper disable once SuggestVarOrType_SimpleTypes
Encoding textEncoding = Encoding.GetEncoding(textEncodingString);

var modeBehavior = args.Contains("--send") || args.Contains("-s")
    ? PortMode.Send
    : args.Contains("--receive") || args.Contains("-r")
        ? PortMode.Receive
        : PortMode.Send;


var port = args.FirstOrDefault();

if (string.IsNullOrWhiteSpace(port))
{
    PrintUsage();
    return;
}

if (port.Equals("."))
{
    port = SerialPort.GetPortNames().FirstOrDefault(a => a.StartsWith("/dev/tty.usbserial") || a.StartsWith("COM"));
    if (string.IsNullOrWhiteSpace(port))
    {
        Console.WriteLine("No serial port found.");
        return;
    }
}

var blockSize = (args
    .Where(a => a.StartsWith("--block-size="))
    .Select(a => a[13..])
    .FirstOrDefault() ?? args
    .Where(a => a.StartsWith("-b="))
    .Select(a => a[3..])
    .FirstOrDefault()) ?? "1024";

var interval = (args
    .Where(a => a.StartsWith("--interval="))
    .Select(a => a[11..])
    .FirstOrDefault() ?? args
    .Where(a => a.StartsWith("-i="))
    .Select(a => a[3..])
    .FirstOrDefault()) ?? "0";

var parameter = args.Where(a => a.StartsWith("--parameter="))
    .Select(a => a[12..])
    .FirstOrDefault() ?? args
    .Where(a => a.StartsWith("-p="))
    .Select(a => a[3..])
    .FirstOrDefault();

var file = args.LastOrDefault();
if (string.IsNullOrWhiteSpace(file))
{
    PrintUsage();
    return;
}

SerialPortHelper.SetTransInterval(int.Parse(interval));
SerialPortHelper.SetBlockSize(int.Parse(blockSize));

if (modeDebug)
{
    UsingDebugMode(port, parameter, modeDebugView);
}
else
{
    if (modeBehavior == PortMode.Receive)
        UsingReceivingMode(port, parameter, file);
    else
        UsingSendingMode(port, parameter, file);
}

void HandleDebugReceiving(SerialPort serialPortInstance, string viewMode, Stream? fs)
{
    var dataBlockSize = SerialPortHelper.GetBlockSize();
    var transInterval = SerialPortHelper.GetTransInterval();
    var maxDataBfSize = dataBlockSize * 12;
    var ms = fs is null ? new MemoryStream(maxDataBfSize) : null;

    if (fs is not null)
    {
        Console.WriteLine("Bytes data received redirecting to file: " + fileRedirect);
    }
    else
    {
        Console.Clear();
    }

    var totalReceived = 0;

    while (serialPortInstance.IsOpen)
    {
        Task.Delay(transInterval).Wait();
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
                switch (viewMode)
                {
                    case "hex":
                        PrintBytesData(ms.ToArray());
                        break;
                    case "text":
                        Console.Write(textEncoding.GetString(ms.ToArray()));
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

    ;
}

void UsingDebugMode(string portName, string? portParameter, string viewMode)
{
    var dataBlockSize = SerialPortHelper.GetBlockSize();
    var transInterval = SerialPortHelper.GetTransInterval();
    using Stream? fs = fileRedirect is null ? null : File.Create(fileRedirect);
    using var serialPortInstance = SerialPortHelper.Create(portName, portParameter);

    serialPortInstance.Open();

    // ReSharper disable AccessToDisposedClosure
    using var t = Task.Run(() => HandleDebugReceiving(serialPortInstance, viewMode, fs));
    // ReSharper restore AccessToDisposedClosure

    try
    {
        string? input;
        while ((input = Console.ReadLine()) != "\0")
        {
            if (string.IsNullOrWhiteSpace(input)) continue;

            var data = GetSendingData(input, textEncoding, viewMode);
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
                    Task.Delay(transInterval).Wait();
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

    byte[] GetSendingData(string raw, Encoding encoding, string mode = "text")
    {
        return mode == "hex" ? Convert.FromHexString(raw) : encoding.GetBytes(raw);
    }
}

void UsingReceivingMode(string portName, string? portParameter, string fileToReceive)
{
    using var serialPortInstance = SerialPortHelper.Create(portName, portParameter);

    PrintPortInfo(serialPortInstance);

    serialPortInstance.Open();
    while (true)
    {
        try
        {
            Console.WriteLine("Listening...");

            // clear buffer
            serialPortInstance.DiscardInBuffer();
            serialPortInstance.DiscardOutBuffer();

            // receive file
            SerialPortHelper.ReceiveFile(serialPortInstance, fileToReceive, modeShowDetail);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }

        if (modeKeepOpen) continue;
        break;
    }
}

void UsingSendingMode(string portName, string? portParameter, string fileToSend)
{
    try
    {
        using var serialPortInstance = SerialPortHelper.Create(portName, portParameter);

        PrintPortInfo(serialPortInstance);
        Console.WriteLine("Sending...");

        serialPortInstance.Open();

        // clear buffer
        serialPortInstance.DiscardInBuffer();
        serialPortInstance.DiscardOutBuffer();

        SerialPortHelper.SendFile(serialPortInstance, fileToSend, modeShowDetail);

        serialPortInstance.Close();
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
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
    Console.WriteLine("Using port {0} ({1}, {2}, {3}, {4}, {5} ms, {6} bytes)...",
        serialPort.PortName,
        serialPort.BaudRate,
        serialPort.DataBits,
        serialPort.Parity,
        serialPort.StopBits,
        SerialPortHelper.GetTransInterval(),
        SerialPortHelper.GetBlockSize());
}

void PrintUsage()
{
    Console.WriteLine("Serial File Tool v1.0.0");
    Console.WriteLine("");

    Console.WriteLine("Usage (list ports):");
    Console.WriteLine("    sfr <--list-ports | -l>");
    Console.WriteLine();

    Console.WriteLine("Usage (send):");
    Console.WriteLine("    sfr <PORT> [options] <file>");
    Console.WriteLine();

    Console.WriteLine("Usage (receive):");
    Console.WriteLine("    sfr <PORT> <--receive | -r> [options] <dir>");
    Console.WriteLine();

    Console.WriteLine("Usage (debug mode):");
    Console.WriteLine("    sfr <PORT> <--debug | -D> [options]");
    Console.WriteLine("    Debug Options:");
    Console.WriteLine("        --debug-view=<text|hex|both> | -V=<text|hex|both>");
    Console.WriteLine("            Default: both");
    Console.WriteLine("        --text-encoding=<encoding> | -E=<encoding>");
    Console.WriteLine("            Only suitable for text view mode.");
    Console.WriteLine("            Possible values are:");
    Console.WriteLine("                " + string.Join(",", allowedEncodings));
    Console.WriteLine("            Default: us-ascii");
    Console.WriteLine("        --file=<file> | -f=<file> Where bytes data will be saved to.");
    Console.WriteLine("            Only suitable for debug mode.");
    Console.WriteLine("            When specified, result will not be printed to console.");
    Console.WriteLine();

    Console.WriteLine("When port is '.', the first available port will be used.");
    Console.WriteLine();

    Console.WriteLine("Options:");
    Console.WriteLine("    --block-size=<size> | -b=<size>       Set block size (default: 256)");
    Console.WriteLine("    --interval=<ms> | -i=<ms>             Set interval between blocks (default: 20)");
    Console.WriteLine("    --parameter=<B,D,P,S> | -p=<B,D,P,S>  Set port parameter (default: 115200,8,N,1)");
    Console.WriteLine("        B: BaudRate, Possible values are:");
    Console.WriteLine("            110,  300,  600, 1200, 2400, 4800, 9600, 14400, 19200,");
    Console.WriteLine("            28800, 38400, 56000, 57600, *115200, 128000, 256000");
    Console.WriteLine("            *: recommended value");
    Console.WriteLine("        D: DataBits, number of bits per byte, Possible values are:");
    Console.WriteLine("            5, 6, 7, 8");
    Console.WriteLine("        P: Parity, Possible values:");
    Console.WriteLine("            N: None");
    Console.WriteLine("            E: Even");
    Console.WriteLine("            O: Odd");
    Console.WriteLine("            M: Mark");
    Console.WriteLine("            S: Space");
    Console.WriteLine("        S: StopBits, Possible values:");
    Console.WriteLine("            1: One");
    Console.WriteLine("            1.5: OnePointFive");
    Console.WriteLine("            2: Two");
    Console.WriteLine("    --detail | -d                         Show each block detail");
    Console.WriteLine("    --keep-open | -k                      Keep port open and listen for next file,");
    Console.WriteLine("                                          Suitable for receiving mode only.");
    Console.WriteLine("    --help | -h                           Show this help");
    Console.WriteLine();
}