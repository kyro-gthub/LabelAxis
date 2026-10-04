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

/// <summary>Adding, arranging, aligning, transforming and copying objects on the label.</summary>
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
    /// Mirrors the selection. An image flips its own pixels (the model carries flip
    /// flags); everything else mirrors its position inside the selection's bounding
    /// box, which is what flipping a group of objects means.
    /// </summary>
    private void Flip(bool horizontal)
    {
        var targets = _session.Selection.Where(o => !o.Locked).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        RectMm union = targets[0].Bounds;
        foreach (LabelObject o in targets.Skip(1))
        {
            union = union.Union(o.Bounds);
        }

        var commands = new List<IUndoableCommand>();
        foreach (LabelObject obj in targets)
        {
            if (obj is ImageObject image)
            {
                ImageAdjustments before = image.Adjustments.Clone();
                ImageAdjustments after = image.Adjustments.Clone();
                if (horizontal)
                {
                    after.FlipHorizontal = !after.FlipHorizontal;
                }
                else
                {
                    after.FlipVertical = !after.FlipVertical;
                }

                commands.Add(new SetPropertyCommand<ImageAdjustments>(
                    "Flip image", () => image.Adjustments, v => image.Adjustments = v, after, (image.Id, "flip")));
                _ = before;
            }

            if (obj is ShapeObject { Shape: ShapeKind.Custom } custom)
            {
                List<PointMm> mirrored = CustomShape.Mirrored(custom.CustomPoints, horizontal);
                commands.Add(new SetPropertyCommand<List<PointMm>>(
                    "Flip shape", () => custom.CustomPoints, v => custom.CustomPoints = v, mirrored, (custom.Id, "flip")));
            }

            if (targets.Count > 1)
            {
                RectMm b = obj.Bounds;
                RectMm mirrored = horizontal
                    ? b with { X = union.X + union.Width - (b.X - union.X) - b.Width }
                    : b with { Y = union.Y + union.Height - (b.Y - union.Y) - b.Height };
                commands.Add(new TransformObjectCommand(obj, mirrored, obj.RotationDegrees));
            }
        }

        if (commands.Count > 0)
        {
            _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Flip", commands));
            RefreshCanvas();
        }
    }

    private void RotateSelection(double degrees)
    {
        var commands = _session.Selection
            .Where(o => !o.Locked)
            .Select(obj => (IUndoableCommand)new TransformObjectCommand(
                obj, obj.Bounds, ((obj.RotationDegrees + degrees) % 360 + 360) % 360))
            .ToList();

        if (commands.Count > 0)
        {
            _session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Rotate", commands));
            _properties.RefreshAll();
            RefreshCanvas();
        }
    }

    private void AddObject(LabelObject obj)
    {
        _session.Execute(new AddObjectCommand(_session.ActiveLabel, obj));
        _session.SetSelection(new[] { obj });
        RebuildObjectList();
    }

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    private void AddImage()
    {
        string? path = _shell.PickOpenFile("Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*", "Add image");
        if (path is not null)
        {
            AddImageFromFile(path, atMm: null);
        }
    }

    /// <summary>Shared by the Insert&gt;Image dialog and drag-and-drop onto the canvas.</summary>
    private void AddImageFromFile(string path, Core.Units.PointMm? atMm)
    {
        try
        {
            AddImageFromBytes(File.ReadAllBytes(path), Path.GetExtension(path), Path.GetFileName(path), atMm);
        }
        catch (Exception ex)
        {
            _shell.Warn($"Could not load \"{Path.GetFileName(path)}\": {ex.Message}");
        }
    }

    /// <summary>Shared by files, drag-and-drop and bitmaps pasted from other apps.</summary>
    private void AddImageFromBytes(byte[] bytes, string extension, string? name, Core.Units.PointMm? atMm)
    {
        string id = _session.Document.Assets.Add(bytes, extension);
        (int w, int h) = ImageDimensions.Probe(bytes);
        double aspect = h > 0 ? (double)w / h : 1.0;
        double boxW = Math.Min(_session.ActiveLabel.SizeMm.Width - 4, 30);
        double boxH = aspect > 0 ? boxW / aspect : boxW;

        RectMm bounds = atMm is { } at
            ? new RectMm(
                Math.Clamp(at.X - (boxW / 2), 0, Math.Max(0, _session.ActiveLabel.SizeMm.Width - boxW)),
                Math.Clamp(at.Y - (boxH / 2), 0, Math.Max(0, _session.ActiveLabel.SizeMm.Height - boxH)),
                boxW, boxH)
            : NewBounds(boxW, boxH);

        var obj = new ImageObject
        {
            AssetId = id,
            SourceFileName = name,
            SourcePixelWidth = w,
            SourcePixelHeight = h,
            Bounds = bounds,
        };
        _imageSource.Invalidate();
        AddObject(obj);
    }

    private void ReplaceSelectedImage()
    {
        if (_session.Selection.Count != 1 || _session.Selection[0] is not ImageObject img)
        {
            return;
        }

        string? path = _shell.PickOpenFile("Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*", "Replace image");
        if (path is null)
        {
            return;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            string id = _session.Document.Assets.Add(bytes, Path.GetExtension(path));
            (int w, int h) = ImageDimensions.Probe(bytes);
            string name = Path.GetFileName(path);

            var command = new CompositeCommand("Replace image", new IUndoableCommand[]
            {
                new SetPropertyCommand<string>("Replace image", () => img.AssetId, v => img.AssetId = v, id, (img.Id, "assetId")),
                new SetPropertyCommand<string?>("Replace image", () => img.SourceFileName, v => img.SourceFileName = v, name, (img.Id, "sourceFileName")),
                new SetPropertyCommand<int>("Replace image", () => img.SourcePixelWidth, v => img.SourcePixelWidth = v, w, (img.Id, "sourcePixelWidth")),
                new SetPropertyCommand<int>("Replace image", () => img.SourcePixelHeight, v => img.SourcePixelHeight = v, h, (img.Id, "sourcePixelHeight")),
            });
            _session.Execute(command);
            _imageSource.Invalidate();
            _properties.RefreshAll();
            RebuildObjectList();
            StatusText = $"Replaced image with {name}";
        }
        catch (Exception ex)
        {
            _shell.Warn($"Could not load \"{Path.GetFileName(path)}\": {ex.Message}");
        }
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

    private void OnImagesDroppedOnCanvas(IReadOnlyList<string> paths, Core.Units.PointMm atMm)
    {
        var images = paths.Where(p => ImageExtensions.Contains(Path.GetExtension(p).ToLowerInvariant())).ToList();
        if (images.Count == 0)
        {
            _shell.Warn("Only image files (PNG, JPG, BMP, GIF, TIFF, WEBP) can be dropped onto the canvas.");
            return;
        }

        for (int i = 0; i < images.Count; i++)
        {
            var at = new Core.Units.PointMm(atMm.X + (i * 4), atMm.Y + (i * 4));
            AddImageFromFile(images[i], at);
        }

        StatusText = images.Count == 1 ? $"Added {Path.GetFileName(images[0])}" : $"Added {images.Count} images";
    }

    private void AddDataField()
    {
        string field = _session.Data?.Columns.FirstOrDefault()?.Name ?? "Field";
        AddObject(new TextObject { Name = field, Content = RichTextContent.FromPlainText($"{{{field}}}"), Bounds = NewBounds(35, 7) });
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

    private void GroupSelection()
    {
        string groupId = Guid.NewGuid().ToString("N");
        var commands = _session.Selection
            .Select(o => (IUndoableCommand)new SetPropertyCommand<string?>("Group", () => o.GroupId, v => o.GroupId = v, groupId))
            .ToList();
        _session.Execute(new CompositeCommand("Group", commands));
    }

    private void UngroupSelection()
    {
        var commands = _session.Selection
            .Where(o => o.GroupId is not null)
            .Select(o => (IUndoableCommand)new SetPropertyCommand<string?>("Ungroup", () => o.GroupId, v => o.GroupId = v, null))
            .ToList();
        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand("Ungroup", commands));
        }
    }

    private void Reorder(int delta)
    {
        // One command for the whole selection: one command per object, each remembering its
        // index from before the others moved, jumbled the order when several were selected.
        var command = new ArrangeObjectsCommand(_session.ActiveLabel,
            ObjectStacking.Step(_session.ActiveLabel.Objects, _session.Selection.ToList(), delta));
        if (command.ChangesAnything)
        {
            _session.Execute(command);
            RebuildObjectList();
        }
    }

    private enum AlignEdge
    {
        Left,
        CentreX,
        Right,
        Top,
        CentreY,
        Bottom,
    }

    private bool _alignToLabel;

    /// <summary>
    /// When set, alignment uses the label's edges even with several objects selected.
    /// Otherwise one object aligns to the label and several align to each other.
    /// </summary>
    public bool AlignToLabel
    {
        get => _alignToLabel;
        set => SetField(ref _alignToLabel, value);
    }

    /// <summary>
    /// Aligns by what you see: the edges of each object's rotated/skewed outline,
    /// not its un-rotated box, so a tilted barcode lines up with its neighbours.
    /// </summary>
    private void AlignSelection(AlignEdge edge)
    {
        var targets = _session.Selection.Where(o => !o.Locked).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        RectMm reference = targets.Count == 1 || _alignToLabel
            ? _session.ActiveLabel.Bounds
            : _session.Selection.Select(o => o.RotatedBounds).Aggregate((a, b) => a.Union(b));

        var commands = new List<IUndoableCommand>();
        foreach (LabelObject obj in targets)
        {
            RectMm visual = obj.RotatedBounds;
            double dx = edge switch
            {
                AlignEdge.Left => reference.Left - visual.Left,
                AlignEdge.CentreX => reference.Center.X - visual.Center.X,
                AlignEdge.Right => reference.Right - visual.Right,
                _ => 0,
            };
            double dy = edge switch
            {
                AlignEdge.Top => reference.Top - visual.Top,
                AlignEdge.CentreY => reference.Center.Y - visual.Center.Y,
                AlignEdge.Bottom => reference.Bottom - visual.Bottom,
                _ => 0,
            };

            if (Math.Abs(dx) > 1e-6 || Math.Abs(dy) > 1e-6)
            {
                commands.Add(new TransformObjectCommand(obj, obj.Bounds.Offset(dx, dy), obj.RotationDegrees));
            }
        }

        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand("Align", commands));
        }
    }

    /// <summary>
    /// Equal gaps between objects: the outermost two stay put and everything in
    /// between is spaced so the space between neighbours is the same.
    /// </summary>
    private void DistributeSelection(bool horizontal)
    {
        var targets = _session.Selection.Where(o => !o.Locked).ToList();
        if (targets.Count < 3)
        {
            return;
        }

        var ordered = targets
            .OrderBy(o => horizontal ? o.RotatedBounds.Center.X : o.RotatedBounds.Center.Y)
            .ToList();

        RectMm first = ordered[0].RotatedBounds;
        RectMm last = ordered[^1].RotatedBounds;
        double span = horizontal ? last.Right - first.Left : last.Bottom - first.Top;
        double sizes = ordered.Sum(o => horizontal ? o.RotatedBounds.Width : o.RotatedBounds.Height);
        double gap = (span - sizes) / (ordered.Count - 1);

        var commands = new List<IUndoableCommand>();
        double cursor = horizontal ? first.Left : first.Top;
        foreach (LabelObject obj in ordered)
        {
            RectMm visual = obj.RotatedBounds;
            double delta = cursor - (horizontal ? visual.Left : visual.Top);
            if (Math.Abs(delta) > 1e-6)
            {
                RectMm moved = horizontal ? obj.Bounds.Offset(delta, 0) : obj.Bounds.Offset(0, delta);
                commands.Add(new TransformObjectCommand(obj, moved, obj.RotationDegrees));
            }

            cursor += (horizontal ? visual.Width : visual.Height) + gap;
        }

        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand(horizontal ? "Distribute horizontally" : "Distribute vertically", commands));
        }
    }

    /// <summary>Gives every selected object the width and/or height of the first one selected.</summary>
    private void MatchSize(bool width, bool height)
    {
        if (_session.Selection.Count < 2)
        {
            return;
        }

        RectMm model = _session.Selection[0].Bounds;
        var commands = _session.Selection.Skip(1)
            .Where(o => !o.Locked)
            .Select(o =>
            {
                RectMm b = o.Bounds;
                var size = new SizeMm(width ? model.Width : b.Width, height ? model.Height : b.Height);
                return (IUndoableCommand)new TransformObjectCommand(o, RectMm.FromCenter(b.Center, size), o.RotationDegrees);
            })
            .ToList();

        if (commands.Count > 0)
        {
            _session.Execute(new CompositeCommand("Match size", commands));
        }
    }
}
