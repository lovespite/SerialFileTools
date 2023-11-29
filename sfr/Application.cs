using System.IO.Ports;
using System.Reflection;
using System.Text;
using ConsoleExtension;

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

    private static string DefaultSerialPortParameter => "1600000,8,N,1";

    private static string _portName = string.Empty;

    public static string Protocol { get; set; } = string.Empty;

    public static string PortName
    {
        get => _portName;
        private set
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
                    Logger.Error("Invalid serial port: " + port);
                    Environment.Exit(0x1300);
                }
            }
        }
    }

    public static string PortParameter { get; private set; } = DefaultSerialPortParameter;

    public static string FileName
    {
        get => AppContext.GetData(nameof(FileName)) as string ?? string.Empty;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            
            if (File.Exists(value))
            {
                AppContext.SetData(nameof(FileName), value);
            }
            else
            {
                Logger.Error("File not found: " + value);
                Environment.Exit(0x2100);
            }
        }
    }

    public static string OutputDirectory
    {
        get => AppContext.GetData(nameof(OutputDirectory)) as string ?? string.Empty;
        set => AppContext.SetData(nameof(OutputDirectory), value);
    }

    public static string ProtocolFile
    {
        set
        {
            if (!File.Exists(value)) return;
            try
            {
                var assembly = Assembly.LoadFile(value);
                ControlledStreamProtocol.Static.Protocol.LoadProtocolsFromAssembly(assembly);
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
#if DEBUG
                if (e.StackTrace is not null) Logger.Low(e.StackTrace);
#endif
            }
        }
    }

    public static int BlockSize { get; private set; } = 2048;

    public static bool KeepOpen { get; private set; }

    public static bool Debug { get; private set; }

    public static DebugViewMode DebugView { get; private set; } = DebugViewMode.Both;

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

    public static PortMode Behavior { get; private set; } = PortMode.Send;

    public static string? Redirect { get; private set; }

    //

    public static void LoadArguments(string[] args)
    {
        KeepOpen = args.Contains("--keep-open") || args.Contains("-k");

        Debug = args.Contains("--debug") || args.Contains("-D");

        DebugView = Enum.Parse<DebugViewMode>((args.Where(a => a.StartsWith("--debug-view="))
            .Select(a => a[13..])
            .FirstOrDefault() ?? args.Where(a => a.StartsWith("-V="))
            .Select(a => a[3..])
            .FirstOrDefault()) ?? "both", true);

        TextEncodingStr = (args.Where(a => a.StartsWith("--text-encoding="))
            .Select(a => a[17..])
            .FirstOrDefault() ?? args.Where(a => a.StartsWith("-E="))
            .Select(a => a[3..])
            .FirstOrDefault()) ?? "us-ascii";

        Redirect = args.Where(a => a.StartsWith("--file="))
            .Select(a => a[7..])
            .FirstOrDefault() ?? args.Where(a => a.StartsWith("-f="))
            .Select(a => a[3..])
            .FirstOrDefault();


        Behavior = args.Contains("--send") || args.Contains("-s")
            ? PortMode.Send
            : args.Contains("--receive") || args.Contains("-r")
                ? PortMode.Receive
                : PortMode.Send;

        BlockSize = int.Parse((args
            .Where(a => a.StartsWith("--block-size="))
            .Select(a => a[13..])
            .FirstOrDefault() ?? args
            .Where(a => a.StartsWith("-b="))
            .Select(a => a[3..])
            .FirstOrDefault()) ?? "2048");

        PortParameter = (args.Where(a => a.StartsWith("--parameter="))
            .Select(a => a[12..])
            .FirstOrDefault() ?? args
            .Where(a => a.StartsWith("-p="))
            .Select(a => a[3..])
            .FirstOrDefault()) ?? DefaultSerialPortParameter;

        Protocol = args.Where(a => a.StartsWith("--protocol="))
            .Select(a => a[11..])
            .FirstOrDefault() ?? args.Where(a => a.StartsWith("-P="))
            .Select(a => a[3..])
            .FirstOrDefault() ?? string.Empty;

        ProtocolFile = args.Where(a => a.StartsWith("--protocol-file="))
            .Select(a => a[16..])
            .FirstOrDefault() ?? args.Where(a => a.StartsWith("-F="))
            .Select(a => a[3..])
            .FirstOrDefault() ?? string.Empty;

        PortName = args.First();
    }

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
        Console.WriteLine("        --block-size=<size> | -b=<size> Set block size (default: 2048)");
        Console.WriteLine("        --debug-view=<text|hex|both> | -V=<text|hex|both>");
        Console.WriteLine("            Default: both");
        Console.WriteLine("        --text-encoding=<encoding> | -E=<encoding>");
        Console.WriteLine("            Only suitable for text view mode.");
        Console.WriteLine("            Possible values are:");
        Console.WriteLine("                " + string.Join(",", AllowedEncodings));
        Console.WriteLine("            Default: us-ascii");
        Console.WriteLine("        --file=<file> | -f=<file> Where bytes data will be saved to.");
        Console.WriteLine("            Only suitable for debug mode.");
        Console.WriteLine("            When specified, result will not be printed to console.");
        Console.WriteLine();

        Console.WriteLine("When port is '.', the first available usbserial port will be used.");
        Console.WriteLine();

        Console.WriteLine("Options:");
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
        Console.WriteLine("    --overwrite | -o                      Overwrite existing file");
        Console.WriteLine("    --send | -s                           Send file to port");
        Console.WriteLine("    --receive | -r                        Receive file from port");
        Console.WriteLine("    --protocol=<protocol> | -P=<protocol> Specify protocol to use to send");
        Console.WriteLine("    --protocol-file=<file> | -F=<file>    Load extra protocol from an external");
        Console.WriteLine("    --help | -h                           Show this help");
        Console.WriteLine();
    }
}