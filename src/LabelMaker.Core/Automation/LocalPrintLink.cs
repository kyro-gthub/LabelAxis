using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LabelMaker.Core.Automation;

/// <summary>What the web link needs from the app: carrying out a request, and what can be printed with.</summary>
public interface IPrintRequestHandler
{
    Task<PrintRequestResult> RunAsync(PrintRequest request, string source, CancellationToken cancellation);

    /// <summary>The names of the user's designs, for GET /designs.</summary>
    IReadOnlyList<string> DesignNames();

    /// <summary>The printers installed in Windows, for GET /printers.</summary>
    IReadOnlyList<string> PrinterNames();

    /// <summary>The design a CSV request uses when it names none.</summary>
    string? DefaultDesign { get; }
}

/// <summary>
/// A small web link on this PC that other programs send print requests to:
/// <list type="bullet">
/// <item><c>POST /print</c> with a JSON request (see <see cref="PrintRequest"/>), or with CSV
/// text and <c>?design=…&amp;printer=…&amp;copies=…</c>; <c>output=pdf</c> returns a PDF instead;</item>
/// <item><c>GET /status</c>, <c>/designs</c> and <c>/printers</c>; <c>GET /</c> explains all this.</item>
/// </list>
/// It listens only on this PC unless other computers are allowed, and then they must send
/// the access key. Requests from web pages (which carry an Origin header) are refused, so a
/// website open in a browser cannot make the printer print.
/// </summary>
public sealed class LocalPrintLink : IAsyncDisposable
{
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    private readonly IPrintRequestHandler _handler;
    private readonly int _port;
    private readonly bool _network;
    private readonly string? _key;
    private readonly List<TcpListener> _listeners = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _loops = new();

    public LocalPrintLink(IPrintRequestHandler handler, int port, bool allowOtherComputers, string? accessKey)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        if (port is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "The port must be from 1 to 65535 (0 picks a free one).");
        }

        _port = port;
        _network = allowOtherComputers;
        _key = string.IsNullOrWhiteSpace(accessKey) ? null : accessKey;
        if (_network && _key is null)
        {
            throw new ArgumentException("Other computers can only be allowed with an access key.", nameof(accessKey));
        }
    }

    /// <summary>The address programs on this PC use.</summary>
    public string Address => $"http://localhost:{Port}/";

    /// <summary>The port actually listened on (useful when 0 was asked for in tests).</summary>
    public int Port => _listeners.Count > 0 ? ((IPEndPoint)_listeners[0].LocalEndpoint).Port : _port;

    /// <summary>Starts listening. Throws <see cref="SocketException"/> when the port is taken.</summary>
    public void Start()
    {
        if (_network)
        {
            var any = new TcpListener(IPAddress.IPv6Any, _port);
            any.Server.DualMode = true;
            _listeners.Add(any);
        }
        else
        {
            _listeners.Add(new TcpListener(IPAddress.Loopback, _port));
            if (Socket.OSSupportsIPv6)
            {
                _listeners.Add(new TcpListener(IPAddress.IPv6Loopback, _port));
            }
        }

        foreach (TcpListener listener in _listeners.ToList())
        {
            try
            {
                listener.Start();
            }
            catch (SocketException) when (listener.LocalEndpoint is IPEndPoint { AddressFamily: AddressFamily.InterNetworkV6 } && !_network)
            {
                _listeners.Remove(listener); // IPv6 switched off on this PC: IPv4 is enough
            }
        }

        foreach (TcpListener listener in _listeners)
        {
            _loops.Add(Task.Run(() => AcceptLoop(listener)));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        foreach (TcpListener listener in _listeners)
        {
            listener.Stop();
        }

        try
        {
            await Task.WhenAll(_loops).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or ObjectDisposedException or SocketException)
        {
            // Stopping: whatever was half-done is dropped.
        }

        _stop.Dispose();
    }

    private async Task AcceptLoop(TcpListener listener)
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => Serve(client));
        }
    }

    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(ReadTimeout);
                NetworkStream stream = client.GetStream();
                HttpRequest? request = await HttpRequest.ReadAsync(stream, MaxHeaderBytes, MaxBodyBytes, timeout.Token).ConfigureAwait(false);
                if (request is null)
                {
                    return;
                }

                bool local = client.Client.RemoteEndPoint is IPEndPoint ep && IPAddress.IsLoopback(ep.Address);
                Response response = await Answer(request, local, (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?")
                    .ConfigureAwait(false);
                await response.WriteAsync(stream, _stop.Token).ConfigureAwait(false);
            }
            catch (HttpError error)
            {
                try
                {
                    await Response.Json(error.Status, new { ok = false, message = error.Message }).WriteAsync(client.GetStream(), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
                {
                    // The caller has gone.
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or OperationCanceledException)
            {
                // A dropped or timed-out connection: nothing to answer.
            }
            catch (Exception ex)
            {
                // Anything else still gets an answer, so the program that asked is not left waiting.
                try
                {
                    await Response.Json(500, new { ok = false, message = "Something went wrong: " + ex.Message }).WriteAsync(client.GetStream(), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception writeError) when (writeError is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
                {
                    // The caller has gone.
                }
            }
        }
    }

    private async Task<Response> Answer(HttpRequest request, bool local, string from)
    {
        bool keyGiven = _key is not null &&
                        (string.Equals(request.Header("x-labelaxis-key"), _key, StringComparison.Ordinal) ||
                         string.Equals(request.Query("key"), _key, StringComparison.Ordinal));
        if (!local && !keyGiven)
        {
            return Response.Json(401, new { ok = false, message = "Other computers must send the access key (header X-LabelAxis-Key)." });
        }

        // A web page in a browser always sends Origin: no website may print without the key.
        if (request.Header("origin") is not null && !keyGiven)
        {
            return Response.Json(403, new { ok = false, message = "Requests from web pages are not accepted. Send them from a program, or with the access key." });
        }

        string path = request.Path.TrimEnd('/').ToLowerInvariant();
        switch (request.Method, path)
        {
            case ("GET", ""):
                return Response.Text(200, "text/html; charset=utf-8", HelpPage());
            case ("GET", "/status"):
                return Response.Json(200, new
                {
                    ok = true,
                    app = "LabelAxis",
                    version = typeof(LocalPrintLink).Assembly.GetName().Version?.ToString(3),
                    designs = _handler.DesignNames(),
                    printers = _handler.PrinterNames(),
                });
            case ("GET", "/designs"):
                return Response.Json(200, _handler.DesignNames());
            case ("GET", "/printers"):
                return Response.Json(200, _handler.PrinterNames());
            case ("POST", "/print"):
            case ("POST", "/pdf"):
                return await Print(request, path == "/pdf", local ? "web link" : "web link from " + from).ConfigureAwait(false);
            case (_, "/print" or "/pdf"):
                return Response.Json(405, new { ok = false, message = "Send print requests with POST." });
            default:
                return Response.Json(404, new { ok = false, message = $"There is nothing at {request.Path}. See {Address} for what there is." });
        }
    }

    private async Task<Response> Print(HttpRequest http, bool pdf, string source)
    {
        PrintRequest request;
        try
        {
            string body = Encoding.UTF8.GetString(http.Body);
            string type = http.Header("content-type") ?? string.Empty;
            bool delimited = type.Contains("csv", StringComparison.OrdinalIgnoreCase) ||
                             type.Contains("text/plain", StringComparison.OrdinalIgnoreCase) ||
                             type.Contains("tab-separated", StringComparison.OrdinalIgnoreCase);
            int copies = int.TryParse(http.Query("copies"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) ? c : 1;
            request = delimited
                ? PrintRequest.FromDelimited(body, http.Query("design") ?? _handler.DefaultDesign, http.Query("printer"), copies)
                : PrintRequest.FromJson(body, http.Query("design") ?? _handler.DefaultDesign);
            if (pdf || string.Equals(http.Query("output"), "pdf", StringComparison.OrdinalIgnoreCase))
            {
                request.Output = PrintRequestOutput.Pdf;
            }
        }
        catch (PrintRequestException ex)
        {
            return Response.Json(400, new { ok = false, message = ex.Message });
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or NotSupportedException)
        {
            return Response.Json(400, new { ok = false, message = "The request could not be read: " + ex.Message });
        }

        PrintRequestResult result = await _handler.RunAsync(request, source, _stop.Token).ConfigureAwait(false);
        if (result.Ok && result.Pdf is { } bytes)
        {
            return new Response(200, "application/pdf", bytes);
        }

        return Response.Json(result.Ok ? 200 : 422, new { ok = result.Ok, message = result.Message, labels = result.Labels, reference = result.Reference });
    }

    private string HelpPage()
    {
        string example = WebUtility.HtmlEncode(
            "{\n  \"design\": \"Price tag\",\n  \"printer\": \"\",\n  \"copies\": 1,\n  \"records\": [\n" +
            "    { \"Product\": \"Milk 1 L\", \"Price\": \"1.25\", \"Barcode\": \"5012345678900\" }\n  ]\n}");
        string curl = WebUtility.HtmlEncode(
            $"curl -X POST {Address}print -H \"Content-Type: application/json\" --data @request.json");
        return "<!doctype html><html><head><meta charset=\"utf-8\"><title>LabelAxis print link</title>" +
               "<style>body{font-family:Segoe UI,sans-serif;max-width:760px;margin:40px auto;padding:0 16px;line-height:1.5}" +
               "pre{background:#f3f5f8;padding:12px;border-radius:8px;overflow:auto}</style></head><body>" +
               "<h1>LabelAxis print link</h1><p>Programs on this PC can print labels by sending a request here.</p>" +
               "<h2>Print</h2><p><code>POST /print</code> with a JSON body:</p><pre>" + example + "</pre>" +
               "<p>Each record is one label; its names are the data fields in the design, such as {Product}. " +
               "Leave <code>printer</code> empty for the design's own printer, or use <code>\"thermal\"</code> for its thermal printer settings. " +
               "Add <code>\"output\": \"pdf\"</code> (or send to <code>/pdf</code>) to get a PDF back instead of printing.</p>" +
               "<p>CSV works too: send it with <code>Content-Type: text/csv</code> to <code>/print?design=Price%20tag&amp;copies=1</code>; " +
               "the first row names the fields.</p><pre>" + curl + "</pre>" +
               "<h2>Look up</h2><p><code>GET /status</code>, <code>GET /designs</code>, <code>GET /printers</code>.</p></body></html>";
    }

    // ---- a minimal HTTP/1.1 -------------------------------------------------------------

    private sealed class HttpError : Exception
    {
        public HttpError(int status, string message)
            : base(message) => Status = status;

        public int Status { get; }
    }

    private sealed class HttpRequest
    {
        private readonly Dictionary<string, string> _headers;
        private readonly Dictionary<string, string> _query;

        private HttpRequest(string method, string path, Dictionary<string, string> headers, Dictionary<string, string> query, byte[] body)
        {
            Method = method;
            Path = path;
            _headers = headers;
            _query = query;
            Body = body;
        }

        public string Method { get; }

        public string Path { get; }

        public byte[] Body { get; }

        public string? Header(string name) => _headers.GetValueOrDefault(name);

        public string? Query(string name) => _query.GetValueOrDefault(name) is { Length: > 0 } v ? v : null;

        public static async Task<HttpRequest?> ReadAsync(Stream stream, int maxHeader, int maxBody, CancellationToken cancellation)
        {
            var head = new MemoryStream();
            var one = new byte[1];
            int matched = 0;
            while (matched < 4)
            {
                int read = await stream.ReadAsync(one, cancellation).ConfigureAwait(false);
                if (read == 0)
                {
                    return null;
                }

                head.WriteByte(one[0]);
                matched = one[0] == (matched is 0 or 2 ? '\r' : '\n') ? matched + 1 : one[0] == '\r' ? 1 : 0;
                if (head.Length > maxHeader)
                {
                    throw new HttpError(431, "The request headers are too long.");
                }
            }

            string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            string[] start = lines.Length > 0 ? lines[0].Split(' ') : Array.Empty<string>();
            if (start.Length < 2)
            {
                throw new HttpError(400, "That is not an HTTP request.");
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon > 0)
                {
                    headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                }
            }

            string target = start[1];
            int q = target.IndexOf('?');
            string path = Uri.UnescapeDataString(q < 0 ? target : target[..q]);
            var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (q >= 0)
            {
                foreach (string pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    string key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
                    query[key] = eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                }
            }

            if (headers.TryGetValue("transfer-encoding", out string? te) && te.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpError(411, "Send the request with a Content-Length.");
            }

            int length = headers.TryGetValue("content-length", out string? cl) && int.TryParse(cl, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
            if (length < 0 || length > maxBody)
            {
                throw new HttpError(413, "The request is too big (at most 8 MB).");
            }

            var body = new byte[length];
            int got = 0;
            while (got < length)
            {
                int read = await stream.ReadAsync(body.AsMemory(got), cancellation).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new HttpError(400, "The request ended before its body did.");
                }

                got += read;
            }

            return new HttpRequest(start[0].ToUpperInvariant(), path, headers, query, body);
        }
    }

    private sealed class Response
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

        public Response(int status, string type, byte[] body)
        {
            Status = status;
            Type = type;
            Body = body;
        }

        public int Status { get; }

        public string Type { get; }

        public byte[] Body { get; }

        public static Response Json(int status, object value) =>
            new(status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));

        public static Response Text(int status, string type, string text) => new(status, type, Encoding.UTF8.GetBytes(text));

        public async Task WriteAsync(Stream stream, CancellationToken cancellation)
        {
            string reason = Status switch
            {
                200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found",
                405 => "Method Not Allowed", 411 => "Length Required", 413 => "Payload Too Large", 422 => "Unprocessable Content",
                431 => "Request Header Fields Too Large", 500 => "Internal Server Error", _ => "Error",
            };
            string head = $"HTTP/1.1 {Status} {reason}\r\nContent-Type: {Type}\r\nContent-Length: {Body.Length}\r\n" +
                          "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellation).ConfigureAwait(false);
            await stream.WriteAsync(Body, cancellation).ConfigureAwait(false);
            await stream.FlushAsync(cancellation).ConfigureAwait(false);
        }
    }
}
