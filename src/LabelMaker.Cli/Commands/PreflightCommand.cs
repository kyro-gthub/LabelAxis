using LabelMaker.Core.Data;
using LabelMaker.Core.Data.Import;
using LabelMaker.Core.Model;
using LabelMaker.Core.Preflight;
using LabelMaker.Core.Serialization;

namespace LabelMaker.Cli.Commands;

internal static class PreflightCommand
{
    public static int Run(ArgMap o)
    {
        ProjectDocument document = ProjectFile.Load(o.GetRequired("project"));

        DataSet? data = null;
        if (o.Has("data"))
        {
            var service = new DataImportService();
            RawTable raw = service.ReadRaw(o.GetRequired("data"));
            data = service.BuildDataSet(raw, new DataConnectionModel());
        }

        var env = new PreflightEnvironment
        {
            AssetExists = id => document.Assets.Contains(id),
            ImagePixelSize = id =>
            {
                ProjectAsset? a = document.Assets.Get(id);
                if (a is null)
                {
                    return null;
                }

                (int w, int h) = LabelMaker.Core.Rendering.ImageDimensions.Probe(a.Bytes);
                return w > 0 ? (w, h) : null;
            },
        };

        PreflightReport report = new PreflightChecker(env).Check(document.Project, data);

        foreach (PreflightIssue issue in report.Issues.OrderBy(i => i.Severity))
        {
            string tag = issue.Severity == PreflightSeverity.Error ? "ERROR" : "warn ";
            string loc = issue.RecordIndex is { } r ? $" [record {r + 1}]" : string.Empty;
            Console.WriteLine($"{tag} {issue.Category,-28} {issue.Message}{loc}");
        }

        Console.WriteLine();
        Console.WriteLine($"{report.Errors.Count()} error(s), {report.Warnings.Count()} warning(s).");
        return report.HasErrors ? 4 : 0;
    }
}
