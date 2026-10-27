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

    public RelayCommand OpenPublisherPagesCommand { get; private set; } = null!;

    private void BuildPublisherCommands()
    {
        ImportPublisherCommand = new RelayCommand(() => ImportPublisher(null));
        OpenPublisherPagesCommand = new RelayCommand(OpenPublisherPages);
    }

    /// <summary>
    /// Opens a .pub file as a project with every page laid out as in Publisher. That needs
    /// Microsoft Publisher on this PC; without it, offers the text-and-pictures import.
    /// </summary>
    private void OpenPublisherPages()
    {
        string? path = _shell.PickOpenFile("Microsoft Publisher files|*.pub|All files|*.*", "Open Publisher file");
        if (path is null)
        {
            return;
        }

        if (!Services.PublisherAutomation.IsInstalled)
        {
            if (_shell.Confirm("Bringing in the pages exactly as they are laid out uses Microsoft Publisher, which is not installed on this PC.\n\n" +
                               "Import the text and pictures from the file instead?", "Open Publisher file"))
            {
                ImportPublisher(path);
            }

            return;
        }

        try
        {
            PubLayout layout = _shell.RunJob($"Opening {Path.GetFileName(path)} in Microsoft Publisher", "pages read", 0,
                (progress, token) => Services.PublisherAutomation.ReadAsync(path, progress, token));

            if (layout.Pages.Count == 0)
            {
                _shell.Info("Publisher opened the file but it has no pages.");
                return;
            }

            // A label publication (one label per page) goes on its label sheet, several to a sheet.
            (double widthMm, double heightMm) = PublisherLayoutConverter.PageSizeMm(layout);
            Core.Formtec.LabelTemplate? sheet = PublisherSheet.Find(_env.Templates, widthMm, heightMm, layout.LabelName, _env.Settings.LastTemplateId, layout.Sheet);

            // Publisher's own margins and gaps say exactly where its labels print; a built-in sheet
            // with the same layout is used as it is, any other layout is made into a sheet.
            if (layout.Sheet is { } pubSheet && PublisherSheet.IsLabelPage(widthMm, heightMm)
                && PublisherSheet.FromPublisher(pubSheet, widthMm, heightMm, sheet) is { } exact)
            {
                sheet = exact;
            }
            Core.Serialization.ProjectDocument document = PublisherLayoutConverter.ToProject(layout, sheet);
            LoadSession(document);
            _session.SelectPage(document.Project.Pages[0].Id);

            int shapes = layout.Pages.Sum(p => p.Shapes.Count);
            StatusText = sheet is null
                ? Localization.Localizer.F("Opened {0} page(s) with {1} item(s) from {2}. Save it as a LabelAxis project to keep it.",
                    layout.Pages.Count, shapes, Path.GetFileName(path))
                : Localization.Localizer.F("Opened {0} page(s) with {1} item(s) from {2}, printing on the {3} sheet ({4} labels on {5}); change it in Label manager if you use another sheet. Save it as a LabelAxis project to keep it.",
                    layout.Pages.Count, shapes, Path.GetFileName(path), $"{sheet.Brand} {sheet.ProductCode}", sheet.Grid.Count, Localization.Localizer.T(sheet.EffectivePage.Name));
            if (layout.Notes.Count > 0)
            {
                _shell.ShowTextReport("Publisher import notes",
                    $"{layout.Pages.Count} page(s) came across. A few items needed attention:\n\n" + string.Join("\n", layout.Notes));
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Publisher import cancelled";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            if (_shell.Confirm($"Microsoft Publisher could not open the pages:\n\n{ex.Message}\n\nImport the text and pictures from the file instead?", "Open Publisher file"))
            {
                ImportPublisher(path);
            }
        }
    }

    /// <summary>
    /// "Formtec 3100 sheet (65 labels on A4)", for "printing on the …". The bracket sits inside the
    /// sentence, so each language file can translate the whole sentence with the numbers filled in.
    /// </summary>
    private static string SheetName(Core.Formtec.LabelTemplate sheet) =>
        $"{sheet.Brand} {sheet.ProductCode} sheet ({sheet.Grid.Count} labels on {sheet.EffectivePage.Name})";

    /// <summary>
    /// Projects imported from Publisher before 1.1.1 kept Publisher's page as their sheet: one
    /// label alone on a label-sized page, so each label printed on a sheet of its own. When such a
    /// project opens, offers to put it on the label sheet with labels that size. A file whose owner
    /// said no (they want one label per page) is remembered and not asked about again.
    /// </summary>
    private void OfferPublisherSheet()
    {
        ProjectModel project = _session.Project;
        string? file = _session.Document.FilePath;
        List<string> kept = _env.Settings.PublisherPagesKept;
        if (project.Template is not { Brand: "Publisher", Grid.Count: 1 } page
            || !page.Id.StartsWith("publisher-page-", StringComparison.Ordinal)
            || (file is not null && kept.Contains(file, StringComparer.OrdinalIgnoreCase))
            || PublisherSheet.Find(_env.Templates, page.Grid.LabelWidthMm, page.Grid.LabelHeightMm, null, _env.Settings.LastTemplateId) is not { } sheet)
        {
            return;
        }

        string pageSize = $"{page.Grid.LabelWidthMm:0.##} × {page.Grid.LabelHeightMm:0.##} mm";
        if (!_shell.Confirm(
                $"This project came from Microsoft Publisher with one {pageSize} label on each page, so every label would print on a sheet of its own.\n\n" +
                $"Print it on the {SheetName(sheet)} instead, several labels to a sheet as in Publisher?\n\n" +
                "You can choose a different sheet later in Label manager.\n\n" +
                "Cancel keeps one label per page, and you won't be asked again for this file.",
                "Label sheet"))
        {
            if (file is not null)
            {
                kept.RemoveAll(p => string.Equals(p, file, StringComparison.OrdinalIgnoreCase));
                kept.Add(file);
                if (kept.Count > 200)
                {
                    kept.RemoveRange(0, kept.Count - 200);
                }

                _env.SaveSettings();
            }

            return;
        }

        // One undoable step: the sheet, and every label resized to it (a fraction of a millimetre);
        // the canvas refits whichever way the step is taken, as in ApplyTemplate.
        var labelSize = new SizeMm(sheet.Grid.LabelWidthMm, sheet.Grid.LabelHeightMm);
        var steps = new List<IUndoableCommand>
        {
            new SetPropertyCommand<Core.Formtec.LabelTemplate?>("Change sheet", () => project.Template, v => project.Template = v, sheet),
        };
        foreach (LabelPage labelPage in project.Pages)
        {
            LabelDocument document = labelPage.Document;
            steps.Add(new SetPropertyCommand<SizeMm>("Label size", () => document.SizeMm, v => document.SizeMm = v, labelSize));
        }

        steps.Add(new SetPropertyCommand<SizeMm>("Label size", () => project.MasterLabel.SizeMm,
            v => { project.MasterLabel.SizeMm = v; OnSheetChanged(); }, labelSize));
        _session.Execute(new CompositeCommand("Use label sheet", steps));
        StatusText = Localization.Localizer.F("Printing on the {0} sheet ({1} labels on {2}). Save to keep it.",
            $"{sheet.Brand} {sheet.ProductCode}", sheet.Grid.Count, Localization.Localizer.T(sheet.EffectivePage.Name));
    }

    private void ImportPublisher(string? chosenPath)
    {
        string? path = chosenPath ?? _shell.PickOpenFile("Microsoft Publisher files|*.pub|All files|*.*", "Import from Publisher");
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
