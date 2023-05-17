namespace sfr;

using LibUsbDotNet;
using LibUsbDotNet.Main;
using LibUsbDotNet.WinUsb;
using LibUsbDotNet.LibUsb;

public class UsbHelper
{
    // enum usb devices

    public static IEnumerable<UsbRegistry> ListDevices()
    {
        switch (Environment.OSVersion.Platform)
        {
            case PlatformID.Unix:
            case PlatformID.MacOSX:
                return ListLegacyDevices();
            case PlatformID.Win32NT:
                return ListLibUsbDevices();
            default:
                throw new PlatformNotSupportedException();
        }
    }

    public static IEnumerable<UsbRegistry> ListDevices(bool auto)
    {
        Console.WriteLine("Enumerating USB devices...");
        UsbDevice.ForceLibUsbWinBack = true;
        var devices = UsbDevice.AllDevices;
        return devices?.ToArray() ?? Array.Empty<UsbRegistry>();
    }

    public static IEnumerable<LegacyUsbRegistry> ListLegacyDevices()
    {
        Console.WriteLine("Enumerating legacy devices...");
        var legacyDevices = LegacyUsbRegistry.DeviceList;
        return legacyDevices?.ToArray() ?? Array.Empty<LegacyUsbRegistry>();
    }

    public static IEnumerable<WinUsbRegistry> ListWin32UsbDevices()
    {
        Console.WriteLine("Enumerating Win32 USB devices...");
        var devices = WinUsbRegistry.DeviceList;
        return devices?.ToArray() ?? Array.Empty<WinUsbRegistry>();
    }
    
    public static IEnumerable<LibUsbRegistry> ListLibUsbDevices()
    {
        Console.WriteLine("Enumerating LibUSB devices...");
        var devices = LibUsbRegistry.DeviceList;
        return devices?.ToArray() ?? Array.Empty<LibUsbRegistry>();
    }
}