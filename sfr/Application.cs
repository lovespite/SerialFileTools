using System.IO.Ports; 
using System.Text;

namespace sfr;

public enum DebugViewMode
{
    Hex,
    Text,
    Both
}

public static class Application
{
    private static readonly IReadOnlySet<string> AllowedEncodings = new HashSet<string>
    {
        "us-ascii",
        "utf-8",
        "utf-16",
        "utf-32"
    };

    public const string DefaultSerialPortParameter = "230400,8,N,1";

    private static string _portName = string.Empty;

    public static string PortName
    {
        get => _portName;
        set
        {
            var port = value;
            var ports = new HashSet<string>(SerialPort.GetPortNames());

            if (string.IsNullOrWhiteSpace(port))
            {
                PrintUsage();
                Environment.Exit(0x1000);
            }

            if (port.All(c => '.' == c))
            {
                port = ports
                    .Where(a => a.StartsWith("/dev/tty.usbserial") || a.StartsWith("COM"))
                    .Skip(port.Length - 1)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(port))
                {
                    Console.WriteLine("No serial port found.");
                    Environment.Exit(0x0120);
                }
                else
                {
                    _portName = port;
                }
            }
            else
            {
                if (ports.Contains(port))
                {
                    _portName = port;
                }
                else
                {
                    CConsole.Error("Invalid serial port: " + port);
                }
            }
        }
    }

    public static string PortParameter { get; set; } = DefaultSerialPortParameter;


    private static string _fileName = string.Empty;

    public static string FileName
    {
        get => _fileName;
        set
        {
            if (File.Exists(value)) _fileName = value;
            else
            {
                CConsole.Error("File not found: " + value);
                Environment.Exit(0x2100);
            }
        }
    }
    
    public static ushort ProtocolId { get; set; } = Protocol.Ftp.Id;

    public static string OutputDirectory { get; set; } = string.Empty;

    public static bool Overwrite { get; set; } = false;
    
    public static int BlockSize { get; set; } = 2048;

    public static bool KeepOpen { get; set; }

    public static bool Debug { get; set; }

    public static DebugViewMode DebugView { get; set; } = DebugViewMode.Both;

    public static Encoding TextEncoding { get; private set; } = Encoding.ASCII;

    public static string TextEncodingStr
    {
        set
        {
            if (AllowedEncodings.Contains(value))
            {
                // ReSharper disable once SuggestVarOrType_SimpleTypes
                TextEncoding = Encoding.GetEncoding(value);
            }
            else
            {
                Console.WriteLine($"Invalid text encoding: {value}");
                Console.WriteLine("Allowed encodings: " + string.Join(", ", AllowedEncodings));
                Environment.Exit(0x0100);
            }
        }
    }

    public static PortMode Behavior { get; set; } = PortMode.Send;

    public static string? Redirect { get; set; }

    //

    public static void PrintUsage()
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
        Console.WriteLine("                " + string.Join(",", Application.AllowedEncodings));
        Console.WriteLine("            Default: us-ascii");
        Console.WriteLine("        --file=<file> | -f=<file> Where bytes data will be saved to.");
        Console.WriteLine("            Only suitable for debug mode.");
        Console.WriteLine("            When specified, result will not be printed to console.");
        Console.WriteLine();

        Console.WriteLine("When port is '.', the first available usbserial port will be used.");
        Console.WriteLine();

        Console.WriteLine("Options:");
        Console.WriteLine("    --block-size=<size> | -b=<size>       Set block size (default: 2048)");
        Console.WriteLine("    --parameter=<B,D,P,S> | -p=<B,D,P,S>  Set port parameter (default: " +
                          DefaultSerialPortParameter + ")");
        Console.WriteLine("        B: BaudRate, Possible values are:");
        Console.WriteLine("            115200, 230400, 460800, etc.");
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
}