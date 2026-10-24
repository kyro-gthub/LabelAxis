using ClosedXML.Excel;
using LabelMaker.Core.Diagnostics;

namespace LabelMaker.Core.Data.Import;

/// <summary>
/// Reads a worksheet from an .xlsx workbook via ClosedXML. Values are read as displayed text.
/// ClosedXML also loads every picture and drawing, and refuses some that Excel itself accepts
/// (a picture named with more than 31 characters, for one). Only the cells matter for records,
/// so a workbook it cannot open is read again from a copy in memory without its drawings;
/// the file itself is never changed.
/// </summary>
public sealed class XlsxReader : ITabularReader
{
    public RawTable Read(Stream stream, TabularReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);

        var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        using XLWorkbook workbook = Open(bytes);
        return Read(workbook, options);
    }

    private static XLWorkbook Open(MemoryStream bytes)
    {
        try
        {
            bytes.Position = 0;
            return new XLWorkbook(bytes);
        }
        catch (Exception fault) when (fault is ArgumentException or InvalidOperationException or FormatException or NullReferenceException or KeyNotFoundException)
        {
            if (WithoutDrawings(bytes) is not { } plain)
            {
                throw;
            }

            try
            {
                return new XLWorkbook(plain);
            }
            catch (Exception again) when (again is not OutOfMemoryException)
            {
                // The copy did not help either: report what was wrong with the file itself.
                Faults.Survived(again);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();
                throw;
            }
        }
    }

    /// <summary>A copy of the workbook with the pictures, shapes and charts taken off every sheet, or null when it cannot be made.</summary>
    private static MemoryStream? WithoutDrawings(MemoryStream original)
    {
        try
        {
            var copy = new MemoryStream();
            original.Position = 0;
            original.CopyTo(copy);
            using (var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(copy, isEditable: true))
            {
                foreach (DocumentFormat.OpenXml.Packaging.WorksheetPart part in document.WorkbookPart?.WorksheetParts ?? Enumerable.Empty<DocumentFormat.OpenXml.Packaging.WorksheetPart>())
                {
                    foreach (DocumentFormat.OpenXml.Spreadsheet.Drawing drawing in part.Worksheet.Elements<DocumentFormat.OpenXml.Spreadsheet.Drawing>().ToList())
                    {
                        drawing.Remove();
                    }

                    if (part.DrawingsPart is { } drawings)
                    {
                        part.DeletePart(drawings);
                    }

                    part.Worksheet.Save();
                }
            }

            copy.Position = 0;
            return copy;
        }
        catch (Exception fault) when (fault is not OutOfMemoryException)
        {
            Faults.Survived(fault);
            return null;
        }
    }

    private static RawTable Read(XLWorkbook workbook, TabularReadOptions options)
    {
        var sheetNames = workbook.Worksheets.Select(w => w.Name).ToList();

        IXLWorksheet sheet = options.WorksheetName is { Length: > 0 } name
            ? workbook.Worksheets.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase))
              ?? workbook.Worksheet(1)
            : workbook.Worksheet(1);

        IXLRange? used = sheet.RangeUsed();
        var rows = new List<IReadOnlyList<string>>();
        if (used is not null)
        {
            int firstCol = used.FirstColumn().ColumnNumber();
            int lastCol = used.LastColumn().ColumnNumber();

            foreach (IXLRangeRow xlRow in used.Rows())
            {
                var values = new string[lastCol - firstCol + 1];
                for (int col = firstCol; col <= lastCol; col++)
                {
                    IXLCell cell = xlRow.Worksheet.Cell(xlRow.RowNumber(), col);
                    values[col - firstCol] = CellText(cell);
                }

                rows.Add(values);
                if (options.MaxRows > 0 && rows.Count >= options.MaxRows)
                {
                    break;
                }
            }
        }

        return new RawTable
        {
            Rows = rows,
            WorksheetNames = sheetNames,
            WorksheetName = sheet.Name,
        };
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        try
        {
            if (cell.DataType == XLDataType.DateTime && cell.Value.IsDateTime)
            {
                return cell.Value.GetDateTime().ToString("yyyy-MM-dd");
            }

            return cell.GetFormattedString();
        }
        catch (Exception fault)
        {
            Faults.Survived(fault);
            return cell.Value.ToString() ?? string.Empty;
        }
    }
}
