using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Serialization;

namespace LabelMaker.Cli.Commands;

internal static class InspectCommand
{
    public static int Run(ArgMap o)
    {
        string path = o.GetRequired("project");
        ProjectDocument document = ProjectFile.Load(path);
        ProjectModel p = document.Project;

        Console.WriteLine($"Project     : {p.Metadata.Title}");
        Console.WriteLine($"Id          : {p.Metadata.Id}");
        Console.WriteLine($"Schema      : {p.SchemaVersion}");
        Console.WriteLine($"Modified    : {p.Metadata.ModifiedUtc:u}");
        Console.WriteLine($"Template    : {(p.Template is { } t ? $"{t.Brand} {t.ProductCode} — {t.Name} ({t.Grid.Columns}x{t.Grid.Rows}, {t.Grid.LabelWidthMm:0.#}x{t.Grid.LabelHeightMm:0.#} mm)" : "(none)")}");
        Console.WriteLine($"Master label: {p.MasterLabel.SizeMm.Width:0.#} x {p.MasterLabel.SizeMm.Height:0.#} mm, {p.MasterLabel.Objects.Count} object(s)");

        foreach (LabelObject obj in p.MasterLabel.Objects)
        {
            string extra = obj switch
            {
                BarcodeObject b => $"{b.Spec.Symbology} \"{b.Spec.Data}\"",
                TextObject txt => $"\"{Truncate(txt.Content.PlainText, 40)}\"",
                ImageObject img => img.SourceFileName ?? img.AssetId,
                ShapeObject s => s.Shape.ToString(),
                _ => string.Empty,
            };
            Console.WriteLine($"  - {obj.Kind,-8} {obj.Bounds.Width:0.#}x{obj.Bounds.Height:0.#}mm  {extra}");
        }

        Console.WriteLine($"Pages       : {p.Pages.Count}");
        Console.WriteLine($"Sequences   : {p.Sequences.Count}");
        Console.WriteLine($"Variables   : {p.Variables.Count}");
        Console.WriteLine($"Data conns  : {p.DataConnections.Count}");
        Console.WriteLine($"Cached data : {document.DataCaches.Count}");
        Console.WriteLine($"Assets      : {document.Assets.Count}");
        return 0;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
