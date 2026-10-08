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

/// <summary>Pictures: adding them from a file, a drop or a paste, and replacing one.</summary>
public sealed partial class MainViewModel
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    private void AddImage()
    {
        string? path = _shell.PickOpenFile("Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*", "Add image");
        if (path is not null)
        {
            AddImageFromFile(path, atMm: null);
        }
    }

    /// <summary>
    /// A picture's size in pixels: read from the header for PNG, JPEG, GIF and BMP, otherwise
    /// by Windows' own decoders (TIFF, WebP). Null when the file is not a picture.
    /// </summary>
    private static (int Width, int Height)? PictureSize(byte[] bytes)
    {
        (int w, int h) = ImageDimensions.Probe(bytes);
        return w > 0 && h > 0 ? (w, h) : Rendering.WpfImageSource.PixelSize(bytes);
    }

    /// <summary>Shared by the Insert&gt;Image dialog and drag-and-drop onto the canvas.</summary>
    private void AddImageFromFile(string path, Core.Units.PointMm? atMm)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (PictureSize(bytes) is null)
            {
                // A file that is not a picture would only ever show as an empty box.
                _shell.Warn("The picture could not be opened:\n\n" + Path.GetFileName(path));
                return;
            }

            AddImageFromBytes(bytes, Path.GetExtension(path), Path.GetFileName(path), atMm);
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            _shell.Warn($"Could not load \"{Path.GetFileName(path)}\": {ex.Message}");
        }
    }

    /// <summary>Shared by files, drag-and-drop and bitmaps pasted from other apps.</summary>
    private void AddImageFromBytes(byte[] bytes, string extension, string? name, Core.Units.PointMm? atMm)
    {
        string id = _session.Document.Assets.Add(bytes, extension);
        (int w, int h) = PictureSize(bytes) ?? (0, 0);
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
            (int w, int h) = PictureSize(bytes) ?? (0, 0);
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
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            _shell.Warn($"Could not load \"{Path.GetFileName(path)}\": {ex.Message}");
        }
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
}
