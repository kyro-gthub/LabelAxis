using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Variables;

namespace LabelMaker.Core.Bulk;

/// <summary>One standalone page placed at a specific sheet slot (spec §15 "print multiple pages in one sheet").</summary>
public sealed class PlacedDocument
{
    public required LabelDocument Document { get; init; }
    public required int SheetIndex { get; init; }
    public required LabelSlot Slot { get; init; }
    public required ResolveContext ResolveContext { get; init; }
}

/// <summary>
/// Places a sequence of distinct <see cref="LabelDocument"/>s (the project's
/// standalone pages, spec §5) across the slots of a Formtec sheet — unlike
/// <see cref="BulkRenderPlan"/>, every slot can show a <em>different design</em>,
/// not the same master label with different data. Pages are laid out in fill
/// order and spill onto additional sheets once a sheet's slots are used up.
/// </summary>
public sealed class MultiDocumentRenderPlan
{
    public required LabelTemplate Template { get; init; }
    public required IReadOnlyList<LabelDocument> Documents { get; init; }

    /// <summary>1-based slot to begin at on the first sheet (skip already-used labels — spec §15).</summary>
    public int StartPosition { get; init; } = 1;

    public IReadOnlySet<int> SkipPositions { get; init; } = new HashSet<int>();

    public DateTime Now { get; init; } = DateTime.Now;

    public int LabelsPerSheet => Template.Grid.Count;

    public IEnumerable<PlacedDocument> Enumerate()
    {
        if (Documents.Count == 0)
        {
            yield break;
        }

        IReadOnlyList<LabelSlot> allSlots = TemplateGeometry.AllSlots(Template);
        int firstSheetSlots = 0;
        for (int pos = 1; pos <= allSlots.Count; pos++)
        {
            if (pos >= StartPosition && !SkipPositions.Contains(pos))
            {
                firstSheetSlots++;
            }
        }

        int ordinal = 0;
        foreach (LabelDocument document in Documents)
        {
            int slotOnSheet;
            int sheetIndex;
            if (ordinal < firstSheetSlots)
            {
                slotOnSheet = NthAvailableFirstSheetSlot(ordinal, allSlots.Count);
                sheetIndex = 0;
            }
            else
            {
                int rest = ordinal - firstSheetSlots;
                slotOnSheet = rest % allSlots.Count;
                sheetIndex = 1 + (rest / allSlots.Count);
            }

            LabelSlot slot = allSlots[slotOnSheet];
            yield return new PlacedDocument
            {
                Document = document,
                SheetIndex = sheetIndex,
                Slot = slot,
                ResolveContext = new ResolveContext
                {
                    RecordNumber = ordinal + 1,
                    RecordCount = Documents.Count,
                    PageNumber = sheetIndex + 1,
                    LabelPosition = slot.Index + 1,
                    TemplateName = Template.Name,
                    Now = Now,
                },
            };

            ordinal++;
        }
    }

    private int NthAvailableFirstSheetSlot(int n, int slotsPerSheet)
    {
        int seen = 0;
        for (int pos = 1; pos <= slotsPerSheet; pos++)
        {
            if (pos >= StartPosition && !SkipPositions.Contains(pos))
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
}

/// <summary>One printed sheet's worth of placed standalone pages.</summary>
public sealed class PlannedMultiSheet
{
    public required int SheetIndex { get; init; }
    public required IReadOnlyList<PlacedDocument> Documents { get; init; }
}

public static class MultiDocumentSheetLayout
{
    public static IEnumerable<PlannedMultiSheet> PlanSheets(MultiDocumentRenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var current = new List<PlacedDocument>();
        int currentSheet = 0;

        foreach (PlacedDocument doc in plan.Enumerate())
        {
            if (doc.SheetIndex != currentSheet && current.Count > 0)
            {
                yield return new PlannedMultiSheet { SheetIndex = currentSheet, Documents = current.ToArray() };
                current.Clear();
            }

            currentSheet = doc.SheetIndex;
            current.Add(doc);
        }

        if (current.Count > 0)
        {
            yield return new PlannedMultiSheet { SheetIndex = currentSheet, Documents = current.ToArray() };
        }
    }
}
