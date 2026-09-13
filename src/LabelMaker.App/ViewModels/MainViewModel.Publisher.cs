using System.IO;
using LabelMaker.App.Mvvm;
using LabelMaker.Core.Data;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Units;
using LabelMaker.Data;
using LabelMaker.Data.Publisher;

namespace LabelMaker.App.ViewModels;

/// <summary>Bringing the text and pictures of a Microsoft Publisher file into the label or the records.</summary>
public sealed partial class MainViewModel
{
    public RelayCommand ImportPublisherCommand { get; private set; } = null!;

    private void BuildPublisherCommands() => ImportPublisherCommand = new RelayCommand(ImportPublisher);

    private void ImportPublisher()
    {
        string? path = _shell.PickOpenFile("Microsoft Publisher files|*.pub|All files|*.*", "Import from Publisher");
        if (path is null)
        {
            return;
        }

        try
        {
            PublisherContent content = _shell.RunJob($"Reading {Path.GetFileName(path)}", "", 0,
                (_, token) => Task.Run(() => PublisherReader.Read(path), token));

            if (content.Stories.Count == 0 && content.Images.Count == 0)
            {
                _shell.Info("No text or pictures could be read from this Publisher file." +
                            (content.Notes.Count > 0 ? "\n\n" + string.Join("\n", content.Notes) : string.Empty));
                return;
            }

            _shell.ShowPublisherImport(content, path);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Import cancelled";
        }
        catch (DataSourceException ex)
        {
            _shell.Warn(ex.Message, "Import from Publisher");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _shell.Warn("The Publisher file could not be read:\n\n" + ex.Message, "Import from Publisher");
        }
    }

    /// <summary>Adds one text box per story, stacked down the label, as a single undoable step.</summary>
    public int AddTextBoxes(IReadOnlyList<string> texts)
    {
        var objects = new List<LabelObject>();
        SizeMm label = _session.ActiveLabel.SizeMm;
        double width = Math.Max(10, label.Width * 0.84);
        double x = (label.Width - width) / 2;
        double y = Math.Min(2, label.Height * 0.05);

        foreach (string text in texts.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            int lines = Math.Clamp(text.Split('\n').Length, 1, 20);
            double height = Math.Min(Math.Max(4, lines * 4.5), Math.Max(4, label.Height - 1));
            if (y + height > label.Height)
            {
                y = Math.Max(0, label.Height - height);
            }

            objects.Add(new TextObject
            {
                Name = FirstWords(text),
                Content = RichTextContent.FromPlainText(text),
                Bounds = new RectMm(x, y, width, height),
                FontSizePt = 9,
            });
            y += height + 1;
        }

        if (objects.Count == 0)
        {
            return 0;
        }

        _session.Execute(new CompositeCommand(
            objects.Count == 1 ? "Add text from Publisher" : $"Add {objects.Count} text boxes from Publisher",
            objects.Select(o => (IUndoableCommand)new AddObjectCommand(_session.ActiveLabel, o)).ToList()));
        _session.SetSelection(objects);
        RebuildObjectList();
        StatusText = objects.Count == 1 ? "Added 1 text box" : $"Added {objects.Count} text boxes";
        return objects.Count;
    }

    public int AddPictures(IReadOnlyList<PublisherImage> images, string fileName)
    {
        foreach (PublisherImage image in images)
        {
            AddImageFromBytes(image.Bytes, "." + image.Extension, $"{Path.GetFileNameWithoutExtension(fileName)} picture {image.Number}.{image.Extension}", null);
        }

        if (images.Count > 0)
        {
            StatusText = images.Count == 1 ? "Added 1 picture" : $"Added {images.Count} pictures";
        }

        return images.Count;
    }

    /// <summary>
    /// Makes paragraphs the project's records: one record per paragraph, split into fields at
    /// tabs when asked (Publisher tables and tab-aligned lists), optionally naming the fields
    /// from the first paragraph.
    /// </summary>
    public int UseParagraphsAsRecords(IReadOnlyList<string> paragraphs, bool splitAtTabs, bool firstRowIsHeader, string path)
    {
        List<string[]> rows = paragraphs
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => splitAtTabs ? p.Split('\t').Select(c => c.Trim()).ToArray() : new[] { p.Replace('\n', ' ').Trim() })
            .ToList();
        if (rows.Count == 0 || (firstRowIsHeader && rows.Count < 2))
        {
            return 0;
        }

        int width = rows.Max(r => r.Length);
        IReadOnlyList<string> headers;
        if (firstRowIsHeader)
        {
            headers = Enumerable.Range(0, width).Select(i => i < rows[0].Length && rows[0][i].Length > 0 ? rows[0][i] : $"Field{i + 1}").ToList();
            rows.RemoveAt(0);
        }
        else
        {
            headers = width == 1 ? new[] { "Text" } : Enumerable.Range(1, width).Select(i => $"Field{i}").ToList();
        }

        if (!ConfirmReplacingEditedRecords())
        {
            return 0;
        }

        DataSet data = DataSet.FromRows(headers, rows);
        var connection = new DataConnectionModel
        {
            Name = "Publisher · " + Path.GetFileName(path),
            Kind = DataSourceKind.Manual,
            SourcePath = path,
            LastRefreshedUtc = DateTimeOffset.UtcNow,
        };
        UseRecords(data, connection);
        StatusText = $"{data.RowCount:N0} record(s) with {data.Columns.Count} field(s) from {Path.GetFileName(path)}";
        return data.RowCount;
    }

    private static string FirstWords(string text)
    {
        string line = text.Split('\n')[0].Trim();
        return line.Length <= 28 ? line : line[..28].TrimEnd() + "…";
    }
}
