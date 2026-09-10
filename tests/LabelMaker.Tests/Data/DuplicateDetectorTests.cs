using FluentAssertions;
using LabelMaker.Core.Data;
using Xunit;

namespace LabelMaker.Tests.Data;

public sealed class DuplicateDetectorTests
{
    [Fact]
    public void Finds_repeated_values_with_row_indexes()
    {
        var report = DuplicateDetector.Check(
            new[] { "ABC001", "ABC002", "ABC001", "ABC003", "ABC002" }, DuplicatePolicy.Warn);

        report.HasDuplicates.Should().BeTrue();
        report.Groups.Should().HaveCount(2);
        report.Groups[0].Value.Should().Be("ABC001");
        report.Groups[0].RowIndexes.Should().Equal(0, 2);
    }

    [Fact]
    public void Prevent_policy_is_blocking()
    {
        var report = DuplicateDetector.Check(new[] { "x", "x" }, DuplicatePolicy.Prevent);
        report.IsBlocking.Should().BeTrue();
    }

    [Fact]
    public void Ignore_policy_short_circuits()
    {
        var report = DuplicateDetector.Check(new[] { "x", "x" }, DuplicatePolicy.Ignore);
        report.HasDuplicates.Should().BeFalse();
    }

    [Fact]
    public void Describe_produces_readable_warning_lines()
    {
        var report = DuplicateDetector.Check(new[] { "ABC001258", "y", "ABC001258" }, DuplicatePolicy.Warn);
        report.Describe("barcode").Should().ContainSingle()
            .Which.Should().StartWith("Duplicate barcode detected: ABC001258");
    }

    [Fact]
    public void Works_over_an_imported_dataset_column()
    {
        DataSet data = DataSet.FromRows(
            new[] { "Barcode" },
            new[] { new[] { "1" }, new[] { "2" }, new[] { "1" } });

        var report = DuplicateDetector.CheckColumn(data, "Barcode", DuplicatePolicy.Warn);
        report.Groups.Should().ContainSingle();
    }
}
