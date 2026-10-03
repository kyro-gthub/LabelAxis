using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LabelMaker.App.Printing;

/// <summary>A printer found on the local network.</summary>
public sealed record FoundPrinter(IPAddress Address, int Port, string? HostName)
{
    public string AddressText => Port == 9100 ? Address.ToString() : $"{Address}:{Port}";

    public override string ToString() => HostName is { Length: > 0 } name ? $"{AddressText}  ({name})" : AddressText;
}

/// <summary>
/// Looks for label printers on the networks this PC is on (Wi-Fi or cable) by trying the raw
/// printing port, 9100, on each address of the local subnet. Only this PC's own subnets are
/// tried, and at most 254 addresses on each, so a scan takes a few seconds.
/// </summary>
public static class NetworkPrinterFinder
{
    public const int RawPort = 9100;

    public static Task<IReadOnlyList<FoundPrinter>> FindAsync(IProgress<int>? progress, CancellationToken cancellation) =>
        FindAsync(RawPort, progress, cancellation);

    /// <summary>Every address on this PC's networks that answers on <paramref name="port"/> (9100 raw printing, 631 IPP).</summary>
    public static async Task<IReadOnlyList<FoundPrinter>> FindAsync(int port, IProgress<int>? progress, CancellationToken cancellation)
    {
        List<IPAddress> candidates = CandidateAddresses().ToList();
        var found = new List<FoundPrinter>();
        var gate = new SemaphoreSlim(64);
        int done = 0;

        async Task Probe(IPAddress address)
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (await AnswersAsync(address, port, TimeSpan.FromMilliseconds(400), cancellation).ConfigureAwait(false))
                {
                    string? name = await HostNameAsync(address).ConfigureAwait(false);
                    lock (found)
                    {
                        found.Add(new FoundPrinter(address, port, name));
                    }
                }
            }
            finally
            {
                gate.Release();
                progress?.Report(Interlocked.Increment(ref done) * 100 / Math.Max(1, candidates.Count));
            }
        }

        await Task.WhenAll(candidates.Select(Probe)).ConfigureAwait(false);
        return found.OrderBy(f => f.Address.GetAddressBytes(), ByteOrder.Instance).ToList();
    }

    /// <summary>True when something accepts a connection on that address and port in time.</summary>
    public static async Task<bool> AnswersAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken cancellation)
    {
        using var client = new TcpClient(address.AddressFamily);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(address, port, limit.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>Every other address on the IPv4 subnets of this PC's working network adapters (at most a /24 each).</summary>
    internal static IEnumerable<IPAddress> CandidateAddresses()
    {
        var seen = new HashSet<IPAddress>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
                IsVirtual(nic))
            {
                continue;
            }

            foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(info.Address) ||
                    info.Address.GetAddressBytes()[0] == 169)
                {
                    continue;
                }

                foreach (IPAddress a in Subnet(info.Address, info.PrefixLength))
                {
                    if (seen.Add(a))
                    {
                        yield return a;
                    }
                }
            }
        }
    }

    // Virtual machine and container adapters: no label printer lives on those, and they would
    // multiply the time a search takes.
    private static readonly string[] VirtualWords = { "virtual", "vmware", "hyper-v", "vethernet", "virtualbox", "wsl", "docker", "loopback" };

    private static bool IsVirtual(NetworkInterface nic) =>
        VirtualWords.Any(w => nic.Description.Contains(w, StringComparison.OrdinalIgnoreCase) || nic.Name.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>The host addresses of <paramref name="own"/>'s subnet, narrowed to its /24 when the subnet is bigger.</summary>
    internal static IEnumerable<IPAddress> Subnet(IPAddress own, int prefixLength)
    {
        int prefix = Math.Clamp(prefixLength, 24, 30);
        uint ip = ToUInt(own);
        uint mask = uint.MaxValue << (32 - prefix);
        uint network = ip & mask;
        uint broadcast = network | ~mask;
        for (uint a = network + 1; a < broadcast; a++)
        {
            if (a != ip)
            {
                yield return FromUInt(a);
            }
        }
    }

    private static async Task<string?> HostNameAsync(IPAddress address)
    {
        try
        {
            Task<IPHostEntry> lookup = Dns.GetHostEntryAsync(address);
            Task winner = await Task.WhenAny(lookup, Task.Delay(800)).ConfigureAwait(false);
            if (winner == lookup && (await lookup.ConfigureAwait(false)).HostName is { Length: > 0 } name && name != address.ToString())
            {
                return name;
            }

            // Too slow: give up on the name, and do not leave a failed lookup unobserved.
            _ = lookup.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
        }

        return null;
    }

    private static uint ToUInt(IPAddress a)
    {
        byte[] b = a.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    private static IPAddress FromUInt(uint v) => new(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });

    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static readonly ByteOrder Instance = new();

        public int Compare(byte[]? x, byte[]? y)
        {
            for (int i = 0; i < Math.Min(x!.Length, y!.Length); i++)
            {
                int c = x[i].CompareTo(y[i]);
                if (c != 0)
                {
                    return c;
                }
            }

            return x.Length.CompareTo(y.Length);
        }
    }
}
