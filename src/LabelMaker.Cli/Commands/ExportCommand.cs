using LabelMaker.Core.Data;
using LabelMaker.Core.Data.Import;
using LabelMaker.Core.Model;
using LabelMaker.Core.Serialization;
using LabelMaker.Export;
using LabelMaker.Export.Pdf;

namespace LabelMaker.Cli.Commands;

internal static class ExportCommand
{
    public static int Run(ArgMap o)
    {
        string projectPath = o.GetRequired("project");
        string outPath = o.GetRequired("out");

        ProjectDocument document = ProjectFile.Load(projectPath);
        DataSet? data = LoadData(o, document);

        ExportScope scope = o.GetOr("scope", "sheet").ToLowerInvariant() switch
        {
            "labels" or "label" => ExportScope.IndividualLabels,
            "records" or "record" => ExportScope.PerRecord,
            _ => ExportScope.Sheet,
        };

        var request = new ExportRequest
        {
            Project = document.Project,
            Data = data,
            Scope = scope,
            Dpi = o.GetInt("dpi", 300),
            StartPosition = o.GetInt("start-position", 1),
            DrawLabelOutlines = o.GetFlag("outlines"),
            DrawCropMarks = o.GetFlag("crop-marks"),
            Title = document.Project.Metadata.Title,
        };

        if (!outPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("CLI export only writes PDF. Use the desktop app for PNG/JPEG.");
        }

        byte[] pdf = new PdfExporter().Export(request, document.Assets);
        File.WriteAllBytes(outPath, pdf);
        Console.WriteLine($"Wrote {outPath} ({scope}, {data?.RowCount ?? 0} records)");
        return 0;
    }

    private static DataSet? LoadData(ArgMap o, ProjectDocument document)
    {
        if (o.Has("data"))
        {
            string path = o.GetRequired("data");
            var service = new DataImportService();
            RawTable raw = service.ReadRaw(path, new TabularReadOptions { WorksheetName = o.Get("sheet") });
            var connection = new DataConnectionModel
            {
                Kind = path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? DataSourceKind.Xlsx : DataSourceKind.Csv,
                HeaderRowIndex = o.GetInt("header-row", 0),
            };
            return service.BuildDataSet(raw, connection);
        }

        // Fall back to a cached dataset embedded in the project, if any.
        ProjectDataCache? cache = document.DataCaches.FirstOrDefault();
        if (cache is not null)
        {
            var service = new DataImportService();
            RawTable raw = service.ReadRaw(
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes(cache.Csv)), DataSourceKind.Csv);
            return service.BuildDataSet(raw, new DataConnectionModel());
        }

        return null;
    }
}
