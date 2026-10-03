using System.IO;
using System.IO.Ports;
using System.Text;
using Microsoft.Win32;

namespace LabelMaker.App.Printing;

/// <summary>One COM port a label printer could be on.</summary>
/// <param name="PortName">"COM5".</param>
/// <param name="Description">What it is, e.g. "Bluetooth: XP-P323B" or "USB serial".</param>
/// <param name="IsBluetooth">A Bluetooth link (paired printer) rather than a cable.</param>
public sealed record SerialPortInfo(string PortName, string Description, bool IsBluetooth)
{
    public override string ToString() => $"{PortName} — {Description}";
}

/// <summary>
/// Finds COM ports and says which are Bluetooth. Windows gives every paired Bluetooth
/// printer that speaks the serial profile an "outgoing" COM port; that is the one to print
/// to. The details come from the registry and are best effort: a port Windows lists is
/// always offered, even when nothing more is known about it.
/// </summary>
public static class SerialPrinters
{
    private const string SerialComm = @"HARDWARE\DEVICEMAP\SERIALCOMM";
    private const string BluetoothEnum = @"SYSTEM\CurrentControlSet\Enum\BTHENUM";
    private const string BluetoothDevices = @"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices";

    public static IReadOnlyList<SerialPortInfo> List()
    {
        // COM port -> the driver's device name, e.g. "\Device\BthModem0".
        var devices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(SerialComm);
            foreach (string value in key?.GetValueNames() ?? Array.Empty<string>())
            {
                if (key!.GetValue(value) is string port)
                {
                    devices[port] = value;
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        Dictionary<string, (string? Name, bool Incoming)> bluetooth = BluetoothPorts();
        var ports = new SortedSet<string>(devices.Keys.Concat(SafePortNames()), Comparer<string>.Create(ComparePorts));

        var list = new List<SerialPortInfo>();
        foreach (string port in ports)
        {
            if (bluetooth.TryGetValue(port, out (string? Name, bool Incoming) bt))
            {
                // The "incoming" port is for devices that connect to this PC; printing needs the outgoing one.
                string what = bt.Incoming ? "Bluetooth (incoming — not for printing)" : "Bluetooth" + (bt.Name is { Length: > 0 } n ? $": {n}" : string.Empty);
                list.Add(new SerialPortInfo(port, what, IsBluetooth: !bt.Incoming));
                continue;
            }

            string device = devices.GetValueOrDefault(port) ?? string.Empty;
            bool isBt = device.Contains("Bth", StringComparison.OrdinalIgnoreCase);
            string description = isBt ? "Bluetooth"
                : device.Contains("USB", StringComparison.OrdinalIgnoreCase) || device.Contains("VCP", StringComparison.OrdinalIgnoreCase) ||
                  device.Contains("Silab", StringComparison.OrdinalIgnoreCase) || device.Contains("Prolific", StringComparison.OrdinalIgnoreCase) ||
                  device.Contains("CH34", StringComparison.OrdinalIgnoreCase) || device.Contains("FTDI", StringComparison.OrdinalIgnoreCase)
                    ? "USB serial"
                    : "Serial port";
            list.Add(new SerialPortInfo(port, description, isBt));
        }

        // Bluetooth printers first: that is what most people are looking for here.
        return list.OrderByDescending(p => p.IsBluetooth).ToList();
    }

    /// <summary>COM ports created for paired Bluetooth devices, with the device's name when Windows has it.</summary>
    private static Dictionary<string, (string? Name, bool Incoming)> BluetoothPorts()
    {
        var result = new Dictionary<string, (string?, bool)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(BluetoothEnum);
            if (root is null)
            {
                return result;
            }

            foreach (string service in root.GetSubKeyNames())
            {
                using RegistryKey? serviceKey = root.OpenSubKey(service);
                foreach (string instance in serviceKey?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using RegistryKey? parameters = serviceKey!.OpenSubKey(instance + @"\Device Parameters");
                    if (parameters?.GetValue("PortName") is not string port)
                    {
                        continue;
                    }

                    // Instance ids end "&<12 hex digit address>_C00000000"; all zeros is the incoming port.
                    string? address = AddressFrom(instance);
                    bool incoming = address is null || address.Trim('0').Length == 0;
                    result[port] = (incoming ? null : DeviceName(address!), incoming);
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return result;
    }

    internal static string? AddressFrom(string instance)
    {
        int underscore = instance.LastIndexOf('_');
        string head = underscore > 0 ? instance[..underscore] : instance;
        int amp = head.LastIndexOf('&');
        string tail = amp >= 0 ? head[(amp + 1)..] : head;
        return tail.Length == 12 && tail.All(Uri.IsHexDigit) ? tail.ToLowerInvariant() : null;
    }

    private static string? DeviceName(string address)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(BluetoothDevices + "\\" + address);
            return key?.GetValue("Name") is byte[] bytes ? Encoding.UTF8.GetString(bytes).TrimEnd('\0').Trim() : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string[] SafePortNames()
    {
        try
        {
            return SerialPort.GetPortNames();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>COM2 before COM10.</summary>
    private static int ComparePorts(string a, string b)
    {
        static int Number(string s) => int.TryParse(new string(s.Where(char.IsDigit).ToArray()), out int n) ? n : int.MaxValue;
        int byNumber = Number(a).CompareTo(Number(b));
        return byNumber != 0 ? byNumber : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Sends printer commands down a COM port: a paired Bluetooth printer or a serial cable.
/// Opening a Bluetooth port makes Windows connect to the printer, which can take a few
/// seconds, so it happens on a background thread when the first label is sent.
/// </summary>
public sealed class SerialSink : IThermalSink
{
    private readonly SerialPort _port;

    public SerialSink(string portName, int baudRate)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new InvalidOperationException("Choose the printer's COM port. For a Bluetooth printer, pair it in Windows Settings first.");
        }

        _port = new SerialPort(portName.Trim(), Math.Clamp(baudRate, 1200, 921600))
        {
            Handshake = Handshake.None,
            WriteTimeout = 20000,
            ReadTimeout = 2000,
            DtrEnable = true,
            RtsEnable = true,
        };
        Description = portName.Trim();
    }

    public string Description { get; }

    public async Task WriteAsync(byte[] data, CancellationToken cancellation)
    {
        if (!_port.IsOpen)
        {
            await Task.Run(Open, cancellation).ConfigureAwait(false);
        }

        // Small pieces, so cancelling stops promptly on a slow Bluetooth link.
        for (int offset = 0; offset < data.Length; offset += 4096)
        {
            cancellation.ThrowIfCancellationRequested();
            int count = Math.Min(4096, data.Length - offset);
            await Task.Run(() => _port.Write(data, offset, count), cancellation).ConfigureAwait(false);
        }
    }

    public async Task CompleteAsync(CancellationToken cancellation)
    {
        if (_port.IsOpen)
        {
            // Let the last bytes leave before the port (and the Bluetooth link) closes.
            await Task.Run(() =>
            {
                DateTime until = DateTime.UtcNow.AddSeconds(30);
                while (_port.BytesToWrite > 0 && DateTime.UtcNow < until)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Thread.Sleep(50);
                }
            }, cancellation).ConfigureAwait(false);
        }

        Dispose();
    }

    public void Dispose() => _port.Dispose();

    /// <summary>Checks that the port opens (for a Bluetooth printer: that Windows can connect to it).</summary>
    public static Task TestAsync(string portName, int baudRate) => Task.Run(() =>
    {
        using var sink = new SerialSink(portName, baudRate);
        sink.Open();
    });

    private void Open()
    {
        try
        {
            _port.Open();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException($"{Description} is busy: another program has it open. Close it and try again.", ex);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            throw new IOException(
                $"Could not open {Description}: {ex.Message}\n\nFor a Bluetooth printer: check it is switched on, paired in Windows Settings › Bluetooth, " +
                "and that you chose its outgoing COM port.", ex);
        }
    }
}
