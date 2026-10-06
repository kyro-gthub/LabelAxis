using System.Collections.ObjectModel;
using System.IO;
using LabelMaker.App.Controls;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Printing;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Data;
using LabelMaker.Core.Data.Import;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Preflight;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using LabelMaker.Export;
using LabelMaker.Export.Pdf;
using LabelMaker.Export.Svg;

namespace LabelMaker.App.ViewModels;

/// <summary>Importing data and stepping through records.</summary>
public sealed partial class MainViewModel
{
    // ---------- data ----------

    private void ImportData()
    {
        string? path = _shell.PickOpenFile("Data files|*.csv;*.tsv;*.txt;*.xlsx|CSV|*.csv|Excel|*.xlsx|All files|*.*", "Import data");
        if (path is null)
        {
            return;
        }

        try
        {
            var service = new DataImportService();
            var connection = new DataConnectionModel
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Kind = path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? DataSourceKind.Xlsx : DataSourceKind.Csv,
                SourcePath = path,
            };
            RawTable raw = service.ReadRaw(path, new TabularReadOptions());
            DataSet data = service.BuildDataSet(raw, connection);


            connection.LastRefreshedUtc = DateTimeOffset.UtcNow;
            UseRecords(data, connection);
            StatusText = $"Imported {data.RowCount} records, {data.Columns.Count} columns from {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _shell.Warn("Import failed:\n\n" + ex.Message);
        }
    }

    private void DuplicateCheck()
    {
        if (_session.Data is not { RowCount: > 0 } data)
        {
            _shell.Info("Import a data source first.");
            return;
        }

        string col = data.Columns.FirstOrDefault(c => c.Name.Contains("barcode", StringComparison.OrdinalIgnoreCase))?.Name
                     ?? data.Columns.FirstOrDefault(c => c.Name.Contains("serial", StringComparison.OrdinalIgnoreCase))?.Name
                     ?? data.Columns.First().Name;

        DuplicateReport report = DuplicateDetector.CheckColumn(data, col, DuplicatePolicy.Warn);
        string body = report.HasDuplicates
            ? $"Column: {col}\n\n" + string.Join('\n', report.Describe(col))
            : $"No duplicate values found in column '{col}'.";
        _shell.ShowTextReport("Duplicate check", body);
    }

    private void RunPreflight()
    {
        var env = new PreflightEnvironment
        {
            AssetExists = id => _session.Document.Assets.Contains(id),
            ImagePixelSize = id =>
            {
                byte[]? b = _session.Document.Assets.GetBytes(id);
                if (b is null)
                {
                    return null;
                }

                (int w, int h) = ImageDimensions.Probe(b);
                return w > 0 ? (w, h) : null;
            },
            FontInstalled = FontIsInstalled,
        };

        PreflightReport report = new PreflightChecker(env).Check(_session.Project, _session.Data);
        string body = report.IsClean
            ? "No problems found."
            : string.Join("\n\n", report.SummaryLines())
              + $"\n\n{report.Errors.Count()} error(s), {report.Warnings.Count()} warning(s) in total.";
        _shell.ShowTextReport("Preflight check", body);
    }

    private static bool FontIsInstalled(string family)
    {
        if (BundledFonts.Contains(family) || Rendering.CustomFonts.Contains(family))
        {
            return true;    // comes with LabelAxis, or added to it from a font file
        }

        return IsInstalledInWindows(family);
    }

    internal static bool IsInstalledInWindows(string family)
    {
        try
        {
            return System.Windows.Media.Fonts.SystemFontFamilies
                .Any(f => f.Source.Equals(family, StringComparison.OrdinalIgnoreCase) ||
                          f.FamilyNames.Values.Any(n => n.Equals(family, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception)
        {
            return true;
        }
    }
}
