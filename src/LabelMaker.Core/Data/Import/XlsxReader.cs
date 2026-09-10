using ClosedXML.Excel;

namespace LabelMaker.Core.Data.Import;

/// <summary>Reads a worksheet from an .xlsx workbook via ClosedXML. Values are read as displayed text.</summary>
public sealed class XlsxReader : ITabularReader
{
    public RawTable Read(Stream stream, TabularReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);

        using var workbook = new XLWorkbook(stream);
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
        catch (Exception)
        {
            return cell.Value.ToString() ?? string.Empty;
        }
    }
}
