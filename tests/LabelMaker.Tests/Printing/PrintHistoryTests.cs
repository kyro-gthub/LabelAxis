using FluentAssertions;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;
using Xunit;

namespace LabelMaker.Tests.Printing;

public sealed class PrintHistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lm-history-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string FilePath => Path.Combine(_dir, "print-history.json");

    [Fact]
    public void Sheet_range_selects_the_sheets_and_clamps_to_the_job()
    {
        int[] sheets = Enumerable.Range(1, 10).ToArray();

        new PrintSettings().ApplySheetRange(sheets).Should().Be((sheets, 1));

        var (slice, first) = new PrintSettings { FirstSheet = 4, LastSheet = 6 }.ApplySheetRange(sheets);
        slice.Should().Equal(4, 5, 6);
        first.Should().Be(4);

        new PrintSettings { FirstSheet = 8 }.ApplySheetRange(sheets).Sheets.Should().Equal(8, 9, 10);
        new PrintSettings { LastSheet = 2 }.ApplySheetRange(sheets).Sheets.Should().Equal(1, 2);
        new PrintSettings { FirstSheet = 50, LastSheet = 3 }.ApplySheetRange(sheets).Sheets.Should().Equal(10);
    }

    [Fact]
    public void Sheet_range_is_not_saved_with_the_project()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new PrintSettings { FirstSheet = 3, LastSheet = 4, Copies = 2 });

        json.Should().NotContain("FirstSheet").And.NotContain("LastSheet").And.NotContain("HasSheetRange");
        json.Should().Contain("\"Copies\":2");
    }

    [Fact]
    public void Store_keeps_newest_first_survives_reload_and_caps_its_size()
    {
        var store = new PrintHistoryStore { MaxEntries = 3 };
        typeof(PrintHistoryStore).GetProperty(nameof(PrintHistoryStore.FilePath))!.SetValue(store, FilePath);

        for (int i = 1; i <= 5; i++)
        {
            store.Add(new PrintHistoryEntry
            {
                ProjectTitle = $"Job {i}",
                StartedUtc = DateTimeOffset.UtcNow.AddMinutes(i),
                Status = i == 5 ? PrintHistoryStatus.Cancelled : PrintHistoryStatus.Completed,
                Sheets = 10,
                SheetsDone = i == 5 ? 4 : 10,
                Settings = new PrintSettings { StartPosition = i },
            });
        }

        store.Entries.Select(e => e.ProjectTitle).Should().Equal("Job 5", "Job 4", "Job 3");

        PrintHistoryStore reloaded = PrintHistoryStore.Load(FilePath);
        reloaded.Entries.Select(e => e.ProjectTitle).Should().Equal("Job 5", "Job 4", "Job 3");
        reloaded.Entries[0].Status.Should().Be(PrintHistoryStatus.Cancelled);
        reloaded.Entries[0].Settings.StartPosition.Should().Be(5);

        reloaded.Remove(reloaded.Entries[1].Id).Should().BeTrue();
        PrintHistoryStore.Load(FilePath).Entries.Should().HaveCount(2);

        reloaded.Clear();
        PrintHistoryStore.Load(FilePath).Entries.Should().BeEmpty();
    }

    [Fact]
    public void A_damaged_history_file_starts_a_fresh_log()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        PrintHistoryStore store = PrintHistoryStore.Load(FilePath);

        store.Entries.Should().BeEmpty();
        store.Add(new PrintHistoryEntry { ProjectTitle = "After" });
        PrintHistoryStore.Load(FilePath).Entries.Should().ContainSingle();
    }

    [Fact]
    public void Reprinting_the_rest_starts_at_the_first_sheet_that_never_went_out()
    {
        var entry = new PrintHistoryEntry
        {
            Status = PrintHistoryStatus.Cancelled,
            Sheets = 20,
            TotalSheets = 30,
            FirstSheet = 11,
            LastSheet = 30,
            SheetsDone = 7,
            PrinterName = "Office laser",
            Settings = new PrintSettings { Copies = 2, StartPosition = 3 },
        };

        entry.IsIncomplete.Should().BeTrue();
        entry.ResumeSheet.Should().Be(18);

        PrintSettings rest = new ReprintRequest(entry, RemainingOnly: true).BuildSettings();
        rest.FirstSheet.Should().Be(18);
        rest.LastSheet.Should().Be(30);
        rest.PrinterName.Should().Be("Office laser");
        rest.Copies.Should().Be(2);
        rest.StartPosition.Should().Be(3);

        PrintSettings again = new ReprintRequest(entry, RemainingOnly: false).BuildSettings();
        again.FirstSheet.Should().Be(11, "the original job only printed sheets 11-30");
        again.LastSheet.Should().Be(30);
    }

    [Fact]
    public void Reprinting_a_whole_job_prints_every_sheet()
    {
        var entry = new PrintHistoryEntry
        {
            Status = PrintHistoryStatus.Completed,
            Sheets = 5,
            TotalSheets = 5,
            FirstSheet = 1,
            LastSheet = 5,
            SheetsDone = 5,
            Settings = new PrintSettings(),
        };

        entry.IsIncomplete.Should().BeFalse();
        PrintSettings settings = new ReprintRequest(entry, RemainingOnly: true).BuildSettings();
        settings.HasSheetRange.Should().BeFalse();
    }
}
