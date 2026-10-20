using LabelMaker.Core.Data;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Sequences;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;

namespace LabelMaker.Core.Bulk;

/// <summary>One label to be produced: which record, where on which sheet, and how to resolve tokens.</summary>
public sealed class PlacedLabel
{
    public required int RecordIndex { get; init; }
    public required int SheetIndex { get; init; }
    public required LabelSlot Slot { get; init; }

    /// <summary>Page-space rectangle (mm) for this label on its sheet.</summary>
    public RectMm PageRectMm => Slot.RectMm;

    public required ResolveContext ResolveContext { get; init; }

    /// <summary>Lazily-created resolver for this label.</summary>
    public VariableResolver CreateResolver() => new(ResolveContext);
}

/// <summary>
/// Streams <see cref="PlacedLabel"/>s for a job without ever cloning the design
/// (spec §27, §37). One master label + a dataset + the current record — the plan
/// just walks records against sheet slots.
/// </summary>
public sealed class BulkRenderPlan
{
    public required LabelTemplate Template { get; init; }
    public required LabelDocument MasterLabel { get; init; }
    public DataSet Data { get; init; } = DataSet.Empty;

    public IReadOnlyList<SequenceDefinition> Sequences { get; init; } = Array.Empty<SequenceDefinition>();
    public IReadOnlyList<CustomVariable> Variables { get; init; } = Array.Empty<CustomVariable>();

    /// <summary>1-based slot to begin at on the first sheet (skip used labels — spec §15).</summary>
    public int StartPosition { get; init; } = 1;

    /// <summary>
    /// The slot printing really begins at: <see cref="StartPosition"/>, or the first slot when that
    /// is not on the sheet (a typing slip, or a number kept from a sheet with more labels), which
    /// would otherwise leave the whole first sheet blank.
    /// </summary>
    private int FirstPosition => StartPosition >= 1 && StartPosition <= LabelsPerSheet ? StartPosition : 1;

    /// <summary>1-based slot positions to skip on the first sheet.</summary>
    public IReadOnlySet<int> SkipPositions { get; init; } = new HashSet<int>();

    /// <summary>0-based record indexes to include (null = all).</summary>
    public IReadOnlyList<int>? RecordSelection { get; init; }

    /// <summary>When true and there is no data, fill every slot with the master label.</summary>
    public bool FillSheetWhenNoData { get; init; } = true;

    public DateTime Now { get; init; } = DateTime.Now;

    public int LabelsPerSheet => Template.Grid.Count;

    /// <summary>Total labels this plan will emit.</summary>
    public int TotalLabels
    {
        get
        {
            int records = ResolveRecordIndexes().Count;
            if (records == 0)
            {
                return FillSheetWhenNoData ? Math.Max(0, LabelsPerSheet - (FirstPosition - 1) - SkipPositions.Count(p => p >= FirstPosition && p <= LabelsPerSheet)) : 0;
            }

            return records;
        }
    }

    public int TotalSheets
    {
        get
        {
            int total = TotalLabels;
            if (total == 0)
            {
                return 0;
            }

            int firstSheetCapacity = Math.Max(0, LabelsPerSheet - (FirstPosition - 1) - SkipPositions.Count(p => p >= FirstPosition && p <= LabelsPerSheet));
            if (total <= firstSheetCapacity)
            {
                return 1;
            }

            return 1 + (int)Math.Ceiling((total - firstSheetCapacity) / (double)LabelsPerSheet);
        }
    }

    public IEnumerable<PlacedLabel> Enumerate()
    {
        List<int> recordIndexes = ResolveRecordIndexes();
        IReadOnlyList<LabelSlot> allSlots = TemplateGeometry.AllSlots(Template);

        // Pre-compute per-record sequence values (advanced once per emitted label).
        var sequenceCursors = Sequences.ToDictionary(s => s.Id, s => (IEnumerator<string>)SequenceGenerator.Enumerate(s).GetEnumerator());

        // Without records, the rest of the one sheet from the start label (TotalLabels): a whole
        // sheet's worth from label 7 would spill onto a second sheet that then printed too.
        IEnumerable<int> emitOrder = recordIndexes.Count > 0
            ? recordIndexes
            : (FillSheetWhenNoData ? Enumerable.Repeat(-1, TotalLabels) : Enumerable.Empty<int>());

        int labelOrdinal = 0;
        foreach (int recordIndex in emitOrder)
        {
            int slotOnSheet = FirstSheetSlotOrdinal(labelOrdinal, allSlots.Count);
            int sheetIndex = FirstSheetSheetIndex(labelOrdinal, allSlots.Count);
            LabelSlot slot = allSlots[slotOnSheet];

            var sequenceValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string id, IEnumerator<string> cursor) in sequenceCursors)
            {
                if (cursor.MoveNext())
                {
                    sequenceValues[id] = cursor.Current;
                    SequenceDefinition? def = Sequences.FirstOrDefault(s => s.Id == id);
                    if (def is not null)
                    {
                        sequenceValues[def.Name] = cursor.Current;
                    }
                }
            }

            IReadOnlyDictionary<string, string>? record = recordIndex >= 0 && recordIndex < Data.RowCount
                ? Data.Rows[recordIndex]
                : null;

            yield return new PlacedLabel
            {
                RecordIndex = recordIndex,
                SheetIndex = sheetIndex,
                Slot = slot,
                ResolveContext = new ResolveContext
                {
                    Record = record,
                    Variables = Variables,
                    SequenceValues = sequenceValues,
                    RecordNumber = recordIndex >= 0 ? recordIndex + 1 : labelOrdinal + 1,
                    RecordCount = recordIndexes.Count > 0 ? recordIndexes.Count : allSlots.Count,
                    PageNumber = sheetIndex + 1,
                    LabelPosition = slot.Index + 1,
                    TemplateName = Template.Name,
                    Now = Now,
                },
            };

            labelOrdinal++;
        }
    }

    private int FirstSheetSlotOrdinal(int labelOrdinal, int slotsPerSheet)
    {
        int firstSheetSlots = FirstSheetSlotIndexes(slotsPerSheet);
        if (labelOrdinal < firstSheetSlots)
        {
            return NthAvailableFirstSheetSlot(labelOrdinal, slotsPerSheet);
        }

        int rest = labelOrdinal - firstSheetSlots;
        return rest % slotsPerSheet;
    }

    private int FirstSheetSheetIndex(int labelOrdinal, int slotsPerSheet)
    {
        int firstSheetSlots = FirstSheetSlotIndexes(slotsPerSheet);
        if (labelOrdinal < firstSheetSlots)
        {
            return 0;
        }

        return 1 + ((labelOrdinal - firstSheetSlots) / slotsPerSheet);
    }

    private int FirstSheetSlotIndexes(int slotsPerSheet)
    {
        int count = 0;
        for (int pos = 1; pos <= slotsPerSheet; pos++)
        {
            if (pos >= FirstPosition && !SkipPositions.Contains(pos))
            {
                count++;
            }
        }

        return count;
    }

    private int NthAvailableFirstSheetSlot(int n, int slotsPerSheet)
    {
        int seen = 0;
        for (int pos = 1; pos <= slotsPerSheet; pos++)
        {
            if (pos >= FirstPosition && !SkipPositions.Contains(pos))
            {
                if (seen == n)
                {
                    return pos - 1;
                }

                seen++;
            }
        }

        return slotsPerSheet - 1;
    }

    private List<int> ResolveRecordIndexes()
    {
        if (Data.RowCount == 0)
        {
            return new List<int>();
        }

        if (RecordSelection is { Count: > 0 })
        {
            return RecordSelection.Where(i => i >= 0 && i < Data.RowCount).Distinct().ToList();
        }

        return Enumerable.Range(0, Data.RowCount).ToList();
    }
}
