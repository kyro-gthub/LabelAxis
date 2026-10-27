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
using LabelMaker.Core.Diagnostics;
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

/// <summary>
/// Adding, deleting, duplicating and pasting objects on the label, and renaming pages.
/// Arranging them is in MainViewModel.Arrange.cs, pictures in MainViewModel.Images.cs.
/// </summary>
public sealed partial class MainViewModel
{
    // ---------- object operations ----------

    /// <summary>
    /// Centres a new object on the label, shrinking the requested size to fit.
    /// The defaults here are sized for a business-card-ish label; on a 25 x 10 mm
    /// sheet an unclamped 40 x 8 mm text box lands mostly outside the artboard,
    /// which is what "the object doesn't match the canvas" looked like.
    /// </summary>
    private RectMm NewBounds(double w, double h)
    {
        var size = _session.ActiveLabel.SizeMm;

        // Leave a visible margin, and never exceed the label itself.
        double maxW = Math.Max(1, size.Width * 0.84);
        double maxH = Math.Max(0.4, size.Height * 0.72);

        if (w > maxW || h > maxH)
        {
            // Scale both axes by the same factor so the intended proportion survives;
            // a zero/near-zero height (the Line default) must not divide by zero.
            double scale = Math.Min(w > 0 ? maxW / w : 1, h > 0.01 ? maxH / h : 1);
            w *= scale;
            h = h > 0.01 ? h * scale : h;
        }

        return new RectMm(
            Math.Max(0, (size.Width - w) / 2),
            Math.Max(0, (size.Height - h) / 2),
            w,
            h);
    }

    /// <summary>
    /// Every entry in the Shapes and Lines toolbar menus funnels through here. The
    /// model already understands N-gons, stars, arrows and dash patterns; this just
    /// names the useful combinations.
    /// </summary>
    private void AddShape(string kind)
    {
        ShapeObject shape = kind.ToLowerInvariant() switch
        {
            "round" => new ShapeObject { Shape = ShapeKind.RoundRectangle, CornerRadiusMm = 2, Bounds = NewBounds(30, 20) },
            "ellipse" => new ShapeObject { Shape = ShapeKind.Ellipse, Bounds = NewBounds(24, 24) },
            "circle" => new ShapeObject { Shape = ShapeKind.Ellipse, Bounds = NewBounds(24, 24) },
            "triangle" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 3, Bounds = NewBounds(24, 24) },
            "diamond" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 4, Bounds = NewBounds(24, 24) },
            "pentagon" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 5, Bounds = NewBounds(24, 24) },
            "hexagon" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 6, Bounds = NewBounds(24, 24) },
            "octagon" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 8, Bounds = NewBounds(24, 24) },
            "star5" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 5, StarInnerRatio = 0.42, Bounds = NewBounds(24, 24) },
            "star6" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 6, StarInnerRatio = 0.5, Bounds = NewBounds(24, 24) },
            "burst" => new ShapeObject { Shape = ShapeKind.Polygon, PolygonPoints = 12, StarInnerRatio = 0.7, Bounds = NewBounds(24, 24) },
            "arrow" => new ShapeObject { Shape = ShapeKind.Arrow, Bounds = NewBounds(40, 0.5), StrokeWidthMm = 0.4 },
            "line" => new ShapeObject { Shape = ShapeKind.Line, Bounds = NewBounds(40, 0.5), StrokeWidthMm = 0.3 },
            "dashed" => new ShapeObject { Shape = ShapeKind.Line, DashStyle = DashStyle.Dash, Bounds = NewBounds(40, 0.5), StrokeWidthMm = 0.3 },
            "dotted" => new ShapeObject { Shape = ShapeKind.Line, DashStyle = DashStyle.Dot, Bounds = NewBounds(40, 0.5), StrokeWidthMm = 0.3 },
            "dashdot" => new ShapeObject { Shape = ShapeKind.Line, DashStyle = DashStyle.DashDot, Bounds = NewBounds(40, 0.5), StrokeWidthMm = 0.3 },
            "hairline" => new ShapeObject { Shape = ShapeKind.Line, Bounds = NewBounds(40, 0.2), StrokeWidthMm = 0.1 },
            // An open line of three dots, the middle one curved: an arch. Edit dots reshapes it.
            "curve" => new ShapeObject
            {
                Shape = ShapeKind.Custom,
                Bounds = NewBounds(40, 12),
                StrokeWidthMm = 0.3,
                ClosedPath = false,
                CustomPoints = new List<PointMm> { new(0, 1), new(0.5, 0), new(1, 1) },
                CurvedPoints = new List<bool> { false, true, false },
            },
            "dashedbox" => new ShapeObject { Shape = ShapeKind.Rectangle, DashStyle = DashStyle.Dash, Bounds = NewBounds(30, 20) },
            _ => new ShapeObject { Shape = ShapeKind.Rectangle, Bounds = NewBounds(30, 20) },
        };

        shape.Stroke = ColorRgba.Black;
        if (shape.StrokeWidthMm <= 0)
        {
            shape.StrokeWidthMm = 0.25;
        }

        AddObject(shape);
    }

    /// <summary>
    /// The GS1 barcode builder: changes the selected barcode, or adds a new one at its natural
    /// size. For a square code the data can also go beside it as text, as medical devices need.
    /// </summary>
    private void ShowGs1Builder()
    {
        BarcodeObject? editing = _session.Selection is [BarcodeObject { Locked: false } b] ? b : null;
        if (_shell.ShowGs1Builder(editing?.Spec) is not { } made)
        {
            return;
        }

        BarcodeSpec spec = made.Spec;
        if (editing is not null)
        {
            // The look the user gave the barcode stays: only what it holds changes.
            BarcodeSpec changed = editing.Spec.Clone();
            changed.Symbology = spec.Symbology;
            changed.Data = spec.Data;
            changed.Gs1Mode = true;
            if (spec.Symbology.Is2D() != editing.Spec.Symbology.Is2D())
            {
                changed.ModuleWidthMm = spec.ModuleWidthMm;
                changed.HumanReadable = spec.HumanReadable;
            }

            var change = new List<IUndoableCommand>
            {
                new SetPropertyCommand<BarcodeSpec>("GS1 barcode", () => editing.Spec, v => editing.Spec = v, changed),
            };
            if (editing.AutoSize)
            {
                // A barcode at its natural size gets a box the size of the new bars, around the same centre.
                Core.Units.SizeMm size = FitToLabel(changed);
                Core.Units.RectMm box = Core.Units.RectMm.FromCenter(editing.Bounds.Center, size);
                change.Add(new SetPropertyCommand<Core.Units.RectMm>("GS1 barcode", () => editing.Bounds, v => editing.Bounds = v, box));
            }

            _session.Execute(change.Count == 1 ? change[0] : new CompositeCommand("GS1 barcode", change));
            RefreshCanvas();
            return;
        }

        Core.Units.SizeMm natural = FitToLabel(spec);
        var barcode = new BarcodeObject { Name = "GS1 barcode", AutoSize = true, Spec = spec, Bounds = NewBounds(natural.Width, natural.Height) };
        if (!spec.Symbology.Is2D() && spec.ModuleWidthMm < 0.25)
        {
            StatusText = Localization.Localizer.F(
                "The barcode was made smaller to fit the label: its narrowest bar is {0} mm. Use a wider label if it does not scan.",
                spec.ModuleWidthMm.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture));
        }
        var added = new List<LabelObject> { barcode };
        var commands = new List<IUndoableCommand> { new AddObjectCommand(_session.ActiveLabel, barcode) };
        if (made.TextBeside is { } text)
        {
            // Beside the code when there is room, otherwise under it; always inside the label, with
            // the text made smaller to fit the space (6 pt at most).
            string[] lines = text.Split('\n');
            Core.Units.SizeMm label = _session.ActiveLabel.SizeMm;
            Core.Units.RectMm code = barcode.Bounds;
            double wanted = (lines.Max(l => l.Length) * 1.25) + 1, tall = lines.Length * 2.6;
            var right = new Core.Units.RectMm(code.Right + 1.5, code.Y, Math.Max(0, label.Width - code.Right - 2), Math.Max(code.Height, Math.Min(tall, label.Height - code.Y - 0.5)));
            var below = new Core.Units.RectMm(Math.Max(0.5, code.X), code.Bottom + 1, Math.Max(0, label.Width - Math.Max(0.5, code.X) - 0.5), Math.Max(0, label.Height - code.Bottom - 1.5));
            double Scale(Core.Units.RectMm r) => Math.Min(r.Width / wanted, r.Height / tall);
            Core.Units.RectMm box = Scale(right) >= Scale(below) ? right : below;
            if (box.Width < 3 || box.Height < 2)
            {
                box = new Core.Units.RectMm(0.5, Math.Max(0.5, label.Height - tall - 0.5), label.Width - 1, Math.Min(tall, label.Height - 1));
            }

            var beside = new TextObject
            {
                Name = "GS1 text",
                Content = RichTextContent.FromPlainText(text),
                FontFamily = "Consolas",
                FontSizePt = 6,
                MinAutoFitPt = 3,
                AutoFit = TextAutoFit.ShrinkToFit,
                WordWrap = false,
                PaddingMm = Core.Model.Objects.Thickness.Zero,
                Bounds = new Core.Units.RectMm(box.X, box.Y, Math.Min(box.Width, wanted), Math.Min(box.Height, tall)),
            };
            added.Add(beside);
            commands.Add(new AddObjectCommand(_session.ActiveLabel, beside));
        }

        _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("GS1 barcode", commands));
        _session.SetSelection(added);
        RebuildObjectList();
    }

    /// <summary>More values than this to compare a scan with would make opening the check slow.</summary>
    private const int MaxCheckedBarcodes = 50_000;

    /// <summary>
    /// The barcodes a scan is compared with: every barcode on every page (the page on screen
    /// first), and for one filled from data its value for every record, so a whole printed batch
    /// of numbered copies or record labels can be checked label by label.
    /// </summary>
    internal IReadOnlyList<(string Name, string Value)> PageBarcodes()
    {
        var found = new List<(string Name, string Value)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        LabelDocument onScreen = _session.ActiveLabel;
        int records = _session.Data?.RowCount ?? 0;
        var current = new Core.Variables.VariableResolver(_session.CurrentResolveContext());
        bool severalPages = _session.Project.Pages.Count > 0;

        IEnumerable<LabelDocument> pages = new[] { onScreen }
            .Concat(Core.Printing.PrintablePages.All(_session.Project).Where(p => !ReferenceEquals(p, onScreen)));
        foreach (LabelDocument page in pages)
        {
            foreach (BarcodeObject barcode in page.Objects.OfType<BarcodeObject>())
            {
                string name = string.IsNullOrWhiteSpace(barcode.Name) ? Localization.Localizer.T("the barcode") : barcode.Name.Trim();
                if (severalPages)
                {
                    name = $"{PageName(page)} › {name}";
                }

                if (records == 0 || !Core.Barcodes.Gs1.Gs1.IsFromData(barcode.Spec.Data))
                {
                    Add(name, current.Resolve(barcode.Spec.Data));
                    continue;
                }

                for (int r = 0; r < records && found.Count < MaxCheckedBarcodes; r++)
                {
                    var resolver = new Core.Variables.VariableResolver(_session.ResolveContextFor(r));
                    Add(Localization.Localizer.F("{0} · record {1}", name, r + 1), resolver.Resolve(barcode.Spec.Data));
                }
            }
        }

        return found;

        void Add(string name, string value)
        {
            if (found.Count < MaxCheckedBarcodes && value.Length > 0 && seen.Add(value))
            {
                found.Add((name, value));
            }
        }
    }

    private string PageName(LabelDocument page) =>
        ReferenceEquals(page, _session.Project.MasterLabel)
            ? MasterPageName
            : _session.Project.Pages.FirstOrDefault(p => ReferenceEquals(p.Document, page))?.Name ?? page.Name;

    /// <summary>
    /// Makes a barcode that keeps its natural size small enough for the label (narrower bars, a
    /// lower height), so the bars never spill past their box; returns its size.
    /// </summary>
    private Core.Units.SizeMm FitToLabel(BarcodeSpec spec)
    {
        if (!BarcodeEngine.TryEncode(spec, spec.Data, out BarcodeSymbol? symbol, out _) || symbol is null)
        {
            return new Core.Units.SizeMm(40, 16);
        }

        Core.Units.SizeMm label = _session.ActiveLabel.SizeMm;
        double maxW = Math.Max(1, label.Width * 0.84), maxH = Math.Max(1, label.Height * 0.72);
        Core.Units.SizeMm natural = BarcodeLayout.NaturalSize(symbol, spec);
        double text = natural.Height - (symbol.Is2D ? natural.Width : spec.BarHeightMm);
        if (symbol.Is2D)
        {
            double side = Math.Min(maxW, maxH - text);
            if (natural.Width > side && side > 0)
            {
                spec.ModuleWidthMm = Math.Round(spec.ModuleWidthMm * side / natural.Width, 3);
            }
        }
        else
        {
            if (natural.Width > maxW)
            {
                spec.ModuleWidthMm = Math.Round(spec.ModuleWidthMm * maxW / natural.Width, 3);
            }

            if (natural.Height > maxH)
            {
                spec.BarHeightMm = Math.Max(3, Math.Round(maxH - text, 1));
            }
        }

        return BarcodeLayout.NaturalSize(symbol, spec);
    }

    private void AddObject(LabelObject obj)
    {
        _session.Execute(new AddObjectCommand(_session.ActiveLabel, obj));
        _session.SetSelection(new[] { obj });
        RebuildObjectList();
    }

    /// <summary>
    /// Toggles the lock on the list row that was clicked, or — when invoked from the
    /// Ctrl+L shortcut with no parameter — on everything currently selected.
    /// </summary>
    private void ToggleObjectLock(ObjectListItem? item)
    {
        var targets = item is not null
            ? item.Members.ToList()
            : _session.Selection.ToList();

        if (targets.Count == 0)
        {
            return;
        }

        bool locking = !targets[0].Locked;
        var commands = targets
            .Select(obj => (IUndoableCommand)new SetPropertyCommand<bool>(
                "Toggle lock", () => obj.Locked, v => obj.Locked = v, locking, (obj.Id, "locked")))
            .ToList();

        _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Toggle lock", commands));
        RebuildObjectList();
        _properties.RefreshAll();
    }


    /// <summary>A text box showing <paramref name="field"/> (a column of the records; the first one when none is named).</summary>
    private void AddDataField(string? field)
    {
        field = !string.IsNullOrWhiteSpace(field) ? field : _session.Data?.Columns.FirstOrDefault()?.Name ?? "Field";

        // A column of barcodes becomes a barcode that scans, of the type chosen (the likely one offered first).
        if (_session.Data is { } data && data.Columns.Any(c => c.Name == field))
        {
            List<string> values = data.Rows.Take(200).Select(r => r.TryGetValue(field, out string? v) ? v ?? string.Empty : string.Empty).ToList();
            if (BarcodeColumns.Suggest(field, values) is { } suggested)
            {
                if (_shell.AskFieldAsBarcode(field, values, suggested) is not { } choice)
                {
                    return;
                }

                if (choice.Barcode is { } symbology)
                {
                    AddDataBarcode(field, symbology, values.FirstOrDefault(v => v.Length > 0) ?? string.Empty);
                    return;
                }
            }
        }

        // One line of text in a size that suits the label, across its width, in the first row
        // nothing else is in, so fields added one after another do not land on top of each other.
        // A long value (a description) shrinks to fit its box instead of growing off the label.
        SizeMm label = _session.ActiveLabel.SizeMm;
        double fontPt = Math.Clamp(Math.Round(label.Height * 0.35 * 2) / 2, 5, 10);
        double lineMm = Math.Round(UnitConverter.PointsToMm(fontPt) * 1.3 + 0.6, 2);
        double margin = Math.Min(2, label.Width * 0.05);
        double width = Math.Max(5, label.Width - (2 * margin));
        double height = Math.Min(lineMm, label.Height);
        (double top, bool free) = RowFor(margin, width, height, margin);
        AddObject(new TextObject
        {
            Name = field,
            Content = RichTextContent.FromPlainText($"{{{field}}}"),
            FontSizePt = fontPt,
            AutoFit = TextAutoFit.ShrinkToFit,
            Bounds = new RectMm(margin, top, width, height),
        });

        if (!free)
        {
            StatusText = Localization.Localizer.F("The label has no free space left, so “{0}” is placed over other objects. Move it, or make room for it.", field);
        }
    }

    /// <summary>
    /// A barcode of <paramref name="symbology"/> showing the column <paramref name="field"/>, sized
    /// for the label from the first record's value, and placed where it covers the least.
    /// </summary>
    private void AddDataBarcode(string field, BarcodeSymbology symbology, string sampleValue)
    {
        var spec = new BarcodeSpec { Symbology = symbology, Data = sampleValue };
        SizeMm size = FitToLabel(spec);
        spec.Data = $"{{{field}}}";

        SizeMm label = _session.ActiveLabel.SizeMm;
        double width = Math.Min(size.Width, label.Width);
        double height = Math.Min(size.Height, label.Height);
        double left = Math.Max(0, (label.Width - width) / 2);
        (double top, bool free) = RowFor(left, width, height, Math.Min(2, label.Height * 0.05));
        AddObject(new BarcodeObject { Name = field, AutoSize = true, Spec = spec, Bounds = new RectMm(left, top, width, height) });

        if (!free)
        {
            StatusText = Localization.Localizer.F("The label has no free space left, so “{0}” is placed over other objects. Move it, or make room for it.", field);
        }
    }

    /// <summary>
    /// Where a new box <paramref name="width"/> × <paramref name="height"/> at <paramref name="left"/>
    /// goes: the first row from the top that no visible object reaches into, or, when the label is
    /// full, the row that covers the least of the others, so new objects do not all land in one spot.
    /// </summary>
    private (double Top, bool Free) RowFor(double left, double width, double height, double margin)
    {
        LabelDocument label = _session.ActiveLabel;
        double lowest = Math.Max(0, label.SizeMm.Height - height);
        var taken = label.Objects.Where(o => o.Visible).Select(o => o.Bounds).ToList();
        double best = 0, leastCovered = double.MaxValue;
        for (double y = Math.Min(margin, lowest); y <= lowest + 0.001; y += 0.25)
        {
            var row = new RectMm(left, y, width, height);
            double covered = taken.Sum(b => Overlap(row, b));
            if (covered < 0.001)
            {
                return (Math.Round(y, 2), true);
            }

            if (covered < leastCovered - 0.001)
            {
                (best, leastCovered) = (y, covered);
            }
        }

        return (Math.Round(best, 2), false);

        static double Overlap(RectMm a, RectMm b) =>
            Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) *
            Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));
    }

    private void DeleteSelection()
    {
        var locked = _session.Selection.Where(o => o.Locked).ToList();
        var deletable = _session.Selection.Where(o => !o.Locked).ToList();
        if (deletable.Count == 0)
        {
            if (locked.Count > 0)
            {
                StatusText = locked.Count == 1
                    ? "That object is locked — unlock it first to delete it."
                    : $"Those {locked.Count} objects are locked — unlock them first to delete them.";
            }

            return;
        }

        _session.Execute(new RemoveObjectsCommand(_session.ActiveLabel, deletable));
        _session.SetSelection(locked);
        RebuildObjectList();
        StatusText = locked.Count > 0
            ? $"Deleted {deletable.Count} object(s); skipped {locked.Count} locked."
            : $"Deleted {deletable.Count} object(s).";
    }

    private void DuplicateSelection()
    {
        // Copies step off the originals; a duplicated group becomes a new group of its own
        // instead of silently joining the original's.
        var clones = PastePlacement.Place(_session.Selection, _session.ActiveLabel).ToList();

        if (clones.Count == 0)
        {
            return;
        }

        _session.Execute(new CompositeCommand("Duplicate",
            clones.Select(c => (IUndoableCommand)new AddObjectCommand(_session.ActiveLabel, c)).ToList()));
        _session.SetSelection(clones);
        RebuildObjectList();
    }

    /// <summary>
    /// Ctrl+V drops a copy near the original; "Paste here" on the canvas menu passes
    /// true so it lands at the click point. The canvas remembers the last right-click
    /// forever, so plain Ctrl+V must not use it or the paste jumps somewhere stale.
    /// </summary>
    private void PasteObjects(bool atContextPoint = false)
    {
        Core.Units.PointMm? atMm = atContextPoint ? _canvas?.ContextMm : null;

        switch (ObjectClipboard.Read())
        {
            case ClipboardContent.Objects objects:
                PasteLabelObjects(objects, atMm);
                break;

            case ClipboardContent.Image image:
                AddImageFromBytes(image.Png, ".png", "Pasted image", atMm);
                StatusText = "Pasted an image from the clipboard";
                break;

            case ClipboardContent.Files files:
                foreach (string path in files.Paths)
                {
                    AddImageFromFile(path, atMm);
                }

                break;

            case ClipboardContent.Text text:
                PasteText(text.Value, atMm);
                break;
        }
    }

    /// <summary>Text from another app becomes a text box sized for a couple of lines.</summary>
    private void PasteText(string value, Core.Units.PointMm? atMm)
    {
        string text = value.Replace("\r\n", "\n").TrimEnd('\n');
        int lines = Math.Clamp(text.Split('\n').Length, 1, 12);
        RectMm bounds = NewBounds(60, 5.5 * lines);
        if (atMm is { } at)
        {
            bounds = bounds with { X = at.X, Y = at.Y };
        }

        AddObject(new TextObject { Content = RichTextContent.FromPlainText(text), Bounds = bounds });
        StatusText = "Pasted text from the clipboard";
    }

    private void PasteLabelObjects(ClipboardContent.Objects clip, Core.Units.PointMm? atMm)
    {
        // The copies keep their layout and groups as one block (see PastePlacement).
        IReadOnlyList<LabelObject> pasted = PastePlacement.Place(clip.Items, _session.ActiveLabel, atMm);
        if (pasted.Count == 0)
        {
            return;
        }

        // Images copied from another project bring their pixels along.
        foreach (ImageObject image in pasted.OfType<ImageObject>())
        {
            if (!_session.Document.Assets.Contains(image.AssetId) &&
                clip.Assets.TryGetValue(image.AssetId, out (string Extension, byte[] Bytes) asset))
            {
                image.AssetId = _session.Document.Assets.Add(asset.Bytes, asset.Extension);
            }
        }

        _imageSource.Invalidate();


        _session.Execute(new CompositeCommand("Paste",
            pasted.Select(c => (IUndoableCommand)new AddObjectCommand(_session.ActiveLabel, c)).ToList()));
        _session.SetSelection(pasted);
        RebuildObjectList();
    }

    /// <summary>
    /// Removes the selected page. Removing the master label (the first row) makes the next
    /// page the master label; the last page left cannot be removed.
    /// </summary>
    private void RemoveActivePage()
    {
        if (_session.IsEditingMaster && _session.Project.Pages.Count == 0)
        {
            StatusText = "A project needs at least one page, so the last page cannot be removed.";
            return;
        }

        string question = _session.IsEditingMaster
            ? $"Remove page \"{ActivePageName}\"? \"{_session.Project.Pages[0].Name}\" becomes the first page (the master label)."
            : $"Remove page \"{ActivePageName}\"?";
        if (!_env.Settings.ConfirmDestructiveActions || _shell.Confirm(question))
        {
            _session.RemoveActivePage();
            StatusText = "Page removed. Undo (Ctrl+Z) brings it back.";
        }
    }

    /// <summary>
    /// Renames whichever page is selected — including the master label, which used to
    /// refuse and so looked like "renaming pages doesn't work" on a project that has
    /// no standalone pages yet.
    /// </summary>
    private void RenameActivePage()
    {
        // The page list shows a text box on the page's own row instead of a pop-up.
        SelectedLeftTab = 1;
        PageRenameRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Asks the view to start renaming the selected page in the page list.</summary>
    public event EventHandler? PageRenameRequested;

    /// <summary>Applies the name typed on the page's row; blank or unchanged names are ignored.</summary>
    public void CommitPageRename(string? name)
    {
        string current = ActivePageName;
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == current)
        {
            return;
        }

        _session.RenameActivePage(name.Trim());
        StatusText = $"Page renamed to \"{name.Trim()}\"";
        OnPropertyChanged(nameof(ActivePageName));
        OnPropertyChanged(nameof(DocumentTabTitle));
    }
}
