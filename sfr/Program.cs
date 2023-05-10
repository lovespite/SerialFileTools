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
var fileRedirect = args.Where(a => a.StartsWith( "--file="))
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

var textEncoding = Encoding.GetEncoding(textEncodingString);

var modeBehavior = args.Contains("--send") || args.Contains("-s")
    ? PortMode.Send
    : args.Contains("--receive") || args.Contains("-r")
        ? PortMode.Receive
        : PortMode.Send;

if (modeListPorts)
{
    SerialPort.GetPortNames().ToList().ForEach(Console.WriteLine);
    return;
}

var port = args.FirstOrDefault();

if (string.IsNullOrWhiteSpace(port))
{
    PrintUsage();
    return;
}

if (port.Equals("."))
{
    port = SerialPort.GetPortNames().FirstOrDefault();
    if (string.IsNullOrWhiteSpace(port))
    {
        Console.WriteLine("No serial port found.");
        return;
    }
}

var blockSize = (args
    .Where(a => a.StartsWith("--block-size="))
    .Select(a => a[14..])
    .FirstOrDefault() ?? args
    .Where(a => a.StartsWith("-b="))
    .Select(a => a[3..])
    .FirstOrDefault()) ?? "256";

var interval = (args
    .Where(a => a.StartsWith("--interval="))
    .Select(a => a[11..])
    .FirstOrDefault() ?? args
    .Where(a => a.StartsWith("-i="))
    .Select(a => a[3..])
    .FirstOrDefault()) ?? "20";

var parameter = args.Where(a => a.StartsWith("--parameter="))
    .Select(a => a[12..])
    .FirstOrDefault() ?? args
    .Where(a => a.StartsWith("-p="))
    .Select(a => a[3..])
    .FirstOrDefault();

var file = args.Skip(1).LastOrDefault();

SerialPortHelper.SetTransInterval(int.Parse(interval));
SerialPortHelper.SetBlockSize(int.Parse(blockSize));

if (string.IsNullOrWhiteSpace(file))
{
    PrintUsage();
    return;
}

if (modeDebug)
{
    UsingSendingDebugMode(port, parameter, modeDebugView);
}
else
{
    switch (modeBehavior)
    {
        case PortMode.Send:
            UsingSendingMode(port, parameter, file);
            break;
        case PortMode.Receive:
            UsingReceivingMode(port, parameter, file);
            break;
    }
}

void UsingSendingDebugMode(string portName, string? portParameter, string viewMode)
{
    var dataBlockSize = SerialPortHelper.GetBlockSize();
    var tansInterval = SerialPortHelper.GetTransInterval();

    using var serialPortInstance = SerialPortHelper.Create(portName, portParameter);
    serialPortInstance.Open();
    Stream? fs = fileRedirect is null ? null : File.Create(fileRedirect);
    
    serialPortInstance.DataReceived += (sender, _) =>
    {
        var sp = (SerialPort)sender;
        var data = new byte[sp.BytesToRead];
        sp.Read(data, 0, data.Length);
        fs?.Write(data);
        fs?.Flush();
        switch (viewMode)
        {
            case "hex":
                PrintBytesData(data);
                break;
            case "text":
                Console.WriteLine(textEncoding.GetString(data));
                break; 
            default:
                PrintBytesData(data, 16, true);
                break;
        }
    };

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
                    Task.Delay(tansInterval).Wait();
                }
            }

            Console.WriteLine($">> {data.Length} bytes sent.");
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
        switch (mode)
        {
            case "hex":
                return Convert.FromHexString(raw);
            default:
                return encoding.GetBytes(raw);
        }
    }
}

void UsingReceivingMode(string portName, string? portParameter, string fileToReceive)
{
    using var serialPortInstance = SerialPortHelper.Create(portName, portParameter);

    PrintPortInfo(serialPortInstance);

    while (true)
    {
        try
        {
            serialPortInstance.Open();
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
        finally
        {
            serialPortInstance.Close();
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
                if (line[i] < 0x20 || line[i] > 0x7e)
                {
                    line[i] = 0x2e;
                }
            }

            lineHexStr += ($" ┃ {textEncoding.GetString(line)}");
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
    Console.WriteLine("            Possible values are:");
    Console.WriteLine("                " + string.Join(",", allowedEncodings));
    Console.WriteLine("            Default: us-ascii"); 
    Console.WriteLine("        --file=<file> | -f=<file> Where bytes data will be saved to.");
    Console.WriteLine("            Only suitable for debug mode.");
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