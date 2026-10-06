using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using LabelMaker.Core.Automation;
using LabelMaker.Core.Data;
using Xunit;

namespace LabelMaker.Tests.Automation;

public sealed class PrintRequestTests
{
    [Fact]
    public void A_json_request_is_read_with_numbers_as_text_and_any_case()
    {
        PrintRequest r = PrintRequest.FromJson("""
            { "Design": "Price tag", "printer": "Zebra", "copies": "2", "output": "pdf",
              "records": [ { "Product": "Milk", "Price": 1.25, "Organic": true } ], "reference": "order-7" }
            """);

        r.Design.Should().Be("Price tag");
        r.Copies.Should().Be(2);
        r.Output.Should().Be(PrintRequestOutput.Pdf);
        r.Records.Should().ContainSingle();
        r.Records[0]["price"].Should().Be("1.25", "field names match in any case");
        r.Records[0]["Organic"].Should().Be("true");
        r.Describe().Should().Be("2 × “Price tag” → PDF (order-7)");
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("{ \"copies\": 1 }", "Say which labels")]
    [InlineData("{ \"design\": \"A\", \"copies\": 0 }", "from 1 to 10000")]
    [InlineData("{ \"design\": \"A\", \"startPosition\": 0 }", "starts at 1")]
    public void A_bad_request_says_why(string json, string expected)
    {
        Action read = () => PrintRequest.FromJson(json);
        read.Should().Throw<PrintRequestException>().Which.Message.Should().Contain(expected);
    }

    [Fact]
    public void The_default_design_is_used_when_none_is_named()
    {
        PrintRequest.FromJson("{ \"records\": [] }", defaultDesign: "Shelf").Design.Should().Be("Shelf");
    }

    [Fact]
    public void A_csv_request_has_one_record_per_row_and_skips_blank_rows()
    {
        PrintRequest r = PrintRequest.FromDelimited("Product,Price\nMilk,1.25\n\nBread,0.95\n", "Price tag", "Zebra", 3);
        r.Records.Should().HaveCount(2);
        r.Records[1]["Product"].Should().Be("Bread");
        r.Printer.Should().Be("Zebra");

        DataSet data = r.ToDataSet();
        data.RowCount.Should().Be(6, "each record three times");
        data.Rows[0]["Product"].Should().Be("Milk");
        data.Rows[3]["Product"].Should().Be("Bread");
    }

    [Fact]
    public void Without_records_the_design_prints_as_it_is_as_many_times_as_asked()
    {
        PrintRequest r = PrintRequest.FromJson("{ \"design\": \"Logo sticker\", \"copies\": 4 }");
        r.ToDataSet().RowCount.Should().Be(4);
    }
}

/// <summary>The web link, talked to over real connections on this PC (nothing leaves it).</summary>
public sealed class LocalPrintLinkTests : IAsyncLifetime
{
    private sealed class FakeHandler : IPrintRequestHandler
    {
        public List<(PrintRequest Request, string Source)> Received { get; } = new();

        public Task<PrintRequestResult> RunAsync(PrintRequest request, string source, CancellationToken cancellation)
        {
            Received.Add((request, source));
            return Task.FromResult(request.Output == PrintRequestOutput.Pdf
                ? new PrintRequestResult(true, "pdf", request.Records.Count) { Pdf = "%PDF-1.7"u8.ToArray() }
                : new PrintRequestResult(true, "printed", Math.Max(1, request.Records.Count) * request.Copies, request.Reference));
        }

        public IReadOnlyList<string> DesignNames() => new[] { "Price tag" };

        public IReadOnlyList<string> PrinterNames() => new[] { "Zebra" };

        public string? DefaultDesign => "Price tag";
    }

    private readonly FakeHandler _handler = new();
    private LocalPrintLink _link = null!;
    private readonly HttpClient _http = new();

    public Task InitializeAsync()
    {
        _link = new LocalPrintLink(_handler, 0, allowOtherComputers: false, accessKey: null);
        _link.Start();
        _http.BaseAddress = new Uri($"http://127.0.0.1:{_link.Port}/");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _link.DisposeAsync();
    }

    [Fact]
    public async Task Status_lists_the_designs_and_printers()
    {
        string status = await _http.GetStringAsync("status");
        status.Should().Contain("\"Price tag\"").And.Contain("\"Zebra\"").And.Contain("LabelAxis");
        (await _http.GetStringAsync("")).Should().Contain("POST /print");
    }

    [Fact]
    public async Task A_json_request_is_handed_on_and_answered()
    {
        HttpResponseMessage reply = await _http.PostAsync("print",
            new StringContent("{ \"design\": \"Price tag\", \"copies\": 2, \"records\": [ { \"Product\": \"Milk\" } ], \"reference\": \"A1\" }",
                Encoding.UTF8, "application/json"));

        reply.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reply.Content.ReadAsStringAsync()).Should().Contain("\"labels\": 2").And.Contain("\"reference\": \"A1\"");
        _handler.Received.Should().ContainSingle().Which.Source.Should().Be("web link");
    }

    [Fact]
    public async Task Csv_goes_to_the_default_design_and_pdf_comes_back_as_a_pdf()
    {
        HttpResponseMessage reply = await _http.PostAsync("print?output=pdf&copies=1",
            new StringContent("Product,Price\nMilk,1.25\n", Encoding.UTF8, "text/csv"));

        reply.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await reply.Content.ReadAsByteArrayAsync()).Should().StartWith("%PDF"u8.ToArray());
        _handler.Received[0].Request.Design.Should().Be("Price tag");
        _handler.Received[0].Request.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task Web_pages_and_bad_requests_are_refused()
    {
        var fromPage = new HttpRequestMessage(HttpMethod.Post, "print")
        {
            Content = new StringContent("{ \"design\": \"Price tag\" }", Encoding.UTF8, "application/json"),
        };
        fromPage.Headers.Add("Origin", "https://example.com");
        (await _http.SendAsync(fromPage)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        HttpResponseMessage bad = await _http.PostAsync("print", new StringContent("{ \"copies\": 0, \"design\": \"x\" }", Encoding.UTF8, "application/json"));
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("from 1 to 10000");

        (await _http.GetAsync("print")).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await _http.GetAsync("nothing-here")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        _handler.Received.Should().BeEmpty();
    }

    [Fact]
    public void Other_computers_need_a_key()
    {
        Action noKey = () => _ = new LocalPrintLink(_handler, 0, allowOtherComputers: true, accessKey: null);
        noKey.Should().Throw<ArgumentException>();
    }
}

public sealed class PrintWatchFolderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "labelaxis-watch-" + Guid.NewGuid().ToString("N"));

    private sealed class Handler : IPrintRequestHandler
    {
        public TaskCompletionSource<PrintRequest> Got { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PrintRequestResult> RunAsync(PrintRequest request, string source, CancellationToken cancellation)
        {
            Got.TrySetResult(request);
            return Task.FromResult(new PrintRequestResult(true, "printed", request.Records.Count));
        }

        public IReadOnlyList<string> DesignNames() => Array.Empty<string>();

        public IReadOnlyList<string> PrinterNames() => Array.Empty<string>();

        public string? DefaultDesign => "Shelf";
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(100);
        }

        return condition();
    }

    [Fact]
    public async Task A_dropped_csv_is_printed_once_and_moved_to_done()
    {
        var handler = new Handler();
        await using var watch = new PrintWatchFolder(handler, _folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, "today.csv"), "Product,Price\nMilk,1.25\nBread,0.95\n");

        PrintRequest request = await handler.Got.Task.WaitAsync(TimeSpan.FromSeconds(15));
        request.Design.Should().Be("Shelf");
        request.Records.Should().HaveCount(2);
        (await Eventually(() => File.Exists(Path.Combine(_folder, PrintWatchFolder.DoneFolder, "today.csv")))).Should().BeTrue();
        File.Exists(Path.Combine(_folder, "today.csv")).Should().BeFalse();
    }

    [Fact]
    public async Task A_bad_request_moves_to_failed_with_the_reason()
    {
        await using var watch = new PrintWatchFolder(new Handler(), _folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, "broken.json"), "{ \"copies\": 0, \"design\": \"x\" }");

        string note = Path.Combine(_folder, PrintWatchFolder.FailedFolder, "broken.json.error.txt");
        (await Eventually(() => File.Exists(note))).Should().BeTrue();
        (await File.ReadAllTextAsync(note)).Should().Contain("from 1 to 10000");
    }
}
