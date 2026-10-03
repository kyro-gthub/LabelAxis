using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using LabelMaker.Core.Printing.Thermal;

namespace LabelMaker.App.Printing;

/// <summary>Somewhere to send thermal printer commands, one label at a time.</summary>
public interface IThermalSink : IDisposable
{
    /// <summary>A short description for progress text and the print log.</summary>
    string Description { get; }

    Task WriteAsync(byte[] data, CancellationToken cancellation);

    /// <summary>Finishes the job (closes the spooler document, flushes the socket or file).</summary>
    Task CompleteAsync(CancellationToken cancellation);
}

public static class ThermalSinks
{
    public static IThermalSink Open(ThermalPrintSettings settings, string? filePath, string jobName) => settings.Destination switch
    {
        ThermalDestination.Network => new NetworkSink(settings.NetworkAddress),
        ThermalDestination.SerialPort => new SerialSink(settings.SerialPortName ?? string.Empty, settings.BaudRate),
        ThermalDestination.File => new FileSink(filePath ?? throw new InvalidOperationException("Choose a file to save to.")),
        _ => new RawSpoolerSink(settings.PrinterName ?? throw new InvalidOperationException("Choose a printer."), jobName),
    };

    /// <summary>Parses "host" or "host:port" (default port 9100).</summary>
    public static (string Host, int Port) ParseAddress(string address)
    {
        string text = address.Trim();
        if (text.Length == 0)
        {
            throw new InvalidOperationException("Enter the printer's network address, for example 192.168.1.50 or 192.168.1.50:9100.");
        }

        int colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':') == colon && int.TryParse(text[(colon + 1)..], out int port) && port is > 0 and < 65536)
        {
            return (text[..colon], port);
        }

        return (text, 9100);
    }
}

/// <summary>
/// Sends bytes to an installed Windows printer as a RAW spooler document, so the driver
/// passes ZPL/TSPL straight to the printer instead of rendering a page.
/// </summary>
public sealed class RawSpoolerSink : IThermalSink
{
    private IntPtr _printer;
    private bool _pageOpen;

    public RawSpoolerSink(string printerName, string jobName)
    {
        Description = printerName;
        if (!NativeMethods.OpenPrinter(printerName, out _printer, IntPtr.Zero))
        {
            throw new InvalidOperationException($"Could not open the printer \"{printerName}\": {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        var doc = new NativeMethods.DocInfo1 { DocName = jobName, DataType = "RAW" };
        if (NativeMethods.StartDocPrinter(_printer, 1, doc) == 0)
        {
            int error = Marshal.GetLastWin32Error();
            NativeMethods.ClosePrinter(_printer);
            _printer = IntPtr.Zero;
            throw new InvalidOperationException(
                $"\"{printerName}\" did not accept raw printer commands ({new Win32Exception(error).Message}). " +
                "Use the printer's own (v3) driver or \"Generic / Text Only\", or send to its network address instead.");
        }

        _pageOpen = NativeMethods.StartPagePrinter(_printer);
    }

    public string Description { get; }

    public Task WriteAsync(byte[] data, CancellationToken cancellation) => Task.Run(() =>
    {
        int offset = 0;
        while (offset < data.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            int count = Math.Min(64 * 1024, data.Length - offset);
            byte[] chunk = offset == 0 && count == data.Length ? data : data[offset..(offset + count)];
            if (!NativeMethods.WritePrinter(_printer, chunk, count, out int written) || written <= 0)
            {
                throw new IOException("The printer stopped accepting data: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }

            offset += written;
        }
    }, cancellation);

    public Task CompleteAsync(CancellationToken cancellation)
    {
        Close();
        return Task.CompletedTask;
    }

    public void Dispose() => Close();

    private void Close()
    {
        if (_printer == IntPtr.Zero)
        {
            return;
        }

        if (_pageOpen)
        {
            NativeMethods.EndPagePrinter(_printer);
            _pageOpen = false;
        }

        NativeMethods.EndDocPrinter(_printer);
        NativeMethods.ClosePrinter(_printer);
        _printer = IntPtr.Zero;
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public sealed class DocInfo1
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string DocName = string.Empty;
            [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string DataType = "RAW";
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool ClosePrinter(IntPtr printer);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int StartDocPrinter(IntPtr printer, int level, [In] DocInfo1 docInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndDocPrinter(IntPtr printer);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool StartPagePrinter(IntPtr printer);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndPagePrinter(IntPtr printer);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool WritePrinter(IntPtr printer, byte[] bytes, int count, out int written);
    }
}

/// <summary>Sends bytes to a network label printer's raw port (JetDirect / port 9100).</summary>
public sealed class NetworkSink : IThermalSink
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;
    private NetworkStream? _stream;

    public NetworkSink(string address)
    {
        (_host, _port) = ThermalSinks.ParseAddress(address);
        Description = $"{_host}:{_port}";
    }

    public string Description { get; }

    public async Task WriteAsync(byte[] data, CancellationToken cancellation)
    {
        if (_stream is null)
        {
            _client = new TcpClient { NoDelay = true, SendTimeout = 15000 };
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await _client.ConnectAsync(_host, _port, connectTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new IOException($"No answer from {Description} within 10 seconds. Check the address and that the printer is on the network.");
            }
            catch (SocketException ex)
            {
                throw new IOException($"Could not connect to {Description}: {ex.Message}", ex);
            }

            _stream = _client.GetStream();
        }

        await _stream.WriteAsync(data, cancellation).ConfigureAwait(false);
    }

    public async Task CompleteAsync(CancellationToken cancellation)
    {
        if (_stream is not null)
        {
            await _stream.FlushAsync(cancellation).ConfigureAwait(false);
        }

        Dispose();
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }
}

/// <summary>Writes the printer commands to a file.</summary>
public sealed class FileSink : IThermalSink
{
    private readonly FileStream _file;

    public FileSink(string path)
    {
        _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16, useAsync: true);
        Description = Path.GetFileName(path);
        FilePath = path;
    }

    public string Description { get; }

    public string FilePath { get; }

    public Task WriteAsync(byte[] data, CancellationToken cancellation) => _file.WriteAsync(data, cancellation).AsTask();

    public async Task CompleteAsync(CancellationToken cancellation)
    {
        await _file.FlushAsync(cancellation).ConfigureAwait(false);
        await _file.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose() => _file.Dispose();
}
