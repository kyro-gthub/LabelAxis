using LabelMaker.Core.Bulk;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;

namespace LabelMaker.Core.Printing;

/// <summary>One printed sheet: its 0-based index and the labels that land on it.</summary>
public sealed class PlannedSheet
{
    public required int SheetIndex { get; init; }
    public required IReadOnlyList<PlacedLabel> Labels { get; init; }
}

/// <summary>
/// Groups a <see cref="BulkRenderPlan"/>'s label stream into physical sheets and
/// applies print-time selection (start position, skip mask, record range /
/// selection). Never materialises more than one sheet's labels at a time when
/// enumerated lazily.
/// </summary>
public static class SheetLayoutEngine
{
    public static IEnumerable<PlannedSheet> PlanSheets(BulkRenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var current = new List<PlacedLabel>();
        int currentSheet = 0;

        foreach (PlacedLabel label in plan.Enumerate())
        {
            if (label.SheetIndex != currentSheet && current.Count > 0)
            {
                yield return new PlannedSheet { SheetIndex = currentSheet, Labels = current.ToArray() };
                current.Clear();
                currentSheet = label.SheetIndex;
            }
            else if (label.SheetIndex != currentSheet)
            {
                currentSheet = label.SheetIndex;
            }

            current.Add(label);
        }

        if (current.Count > 0)
        {
            yield return new PlannedSheet { SheetIndex = currentSheet, Labels = current.ToArray() };
        }
    }

    /// <summary>Build a <see cref="BulkRenderPlan"/> from a project + dataset + print settings.</summary>
    public static BulkRenderPlan CreatePlan(
        ProjectModel project,
        LabelMaker.Core.Data.DataSet? data,
        PrintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(project);
        return CreatePlan(project, data, settings, project.Template
            ?? throw new InvalidOperationException("The project has no Formtec template selected."));
    }

    /// <summary>The same plan laid out on another <paramref name="template"/> (a thermal roll, say).</summary>
    public static BulkRenderPlan CreatePlan(
        ProjectModel project,
        LabelMaker.Core.Data.DataSet? data,
        PrintSettings settings,
        LabelTemplate template)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(template);

        IReadOnlyList<int>? recordSelection = null;
        if (settings.Content == PrintContent.VariableData && data is { RowCount: > 0 })
        {
            if (settings.SelectedRecords.Count > 0)
            {
                recordSelection = settings.SelectedRecords;
            }
            else if (settings.FirstRecord is { } first || settings.LastRecord is { } last)
            {
                int lo = Math.Max(0, settings.FirstRecord ?? 0);
                int hi = Math.Min(data.RowCount - 1, settings.LastRecord ?? data.RowCount - 1);
                recordSelection = lo <= hi ? Enumerable.Range(lo, hi - lo + 1).ToList() : Array.Empty<int>();
            }
        }

        return new BulkRenderPlan
        {
            Template = template,
            MasterLabel = project.MasterLabel,
            Data = settings.Content == PrintContent.VariableData ? data ?? LabelMaker.Core.Data.DataSet.Empty : LabelMaker.Core.Data.DataSet.Empty,
            Sequences = project.Sequences,
            Variables = project.Variables,
            StartPosition = settings.StartPosition,
            SkipPositions = new HashSet<int>(settings.SkipPositions),
            RecordSelection = recordSelection,
            FillSheetWhenNoData = settings.Content == PrintContent.SheetFill,
        };
    }
}
