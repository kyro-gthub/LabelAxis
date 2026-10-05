using LabelMaker.Core.Model.Objects;

namespace LabelMaker.Core.Model;

/// <summary>Which font families a project's labels use.</summary>
public static class FontUsage
{
    /// <summary>Every family named by a text box, a single word, a barcode's digits or a table, on every page.</summary>
    public static IReadOnlySet<string> Families(ProjectModel project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (LabelDocument page in new[] { project.MasterLabel }.Concat(project.Pages.Select(p => p.Document)))
        {
            foreach (LabelObject obj in page.Objects)
            {
                switch (obj)
                {
                    case TextObject text:
                        families.Add(text.FontFamily);
                        foreach (string? family in text.Content.Paragraphs.SelectMany(p => p.Runs).Select(r => r.FontFamily))
                        {
                            if (!string.IsNullOrWhiteSpace(family))
                            {
                                families.Add(family);
                            }
                        }

                        break;
                    case BarcodeObject barcode:
                        families.Add(barcode.Spec.HumanReadableFontFamily);
                        break;
                    case TableObject table:
                        families.Add(table.FontFamily);
                        break;
                }
            }
        }

        families.Remove(string.Empty);
        return families;
    }
}
