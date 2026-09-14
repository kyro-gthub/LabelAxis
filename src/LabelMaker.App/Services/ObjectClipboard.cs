using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Serialization;

namespace LabelMaker.App.Services;

/// <summary>What the Windows clipboard currently offers, in the order paste prefers it.</summary>
public abstract record ClipboardContent
{
    /// <summary>Objects copied from Label Maker (this window, another tab, or another running copy).</summary>
    public sealed record Objects(IReadOnlyList<LabelObject> Items, IReadOnlyDictionary<string, (string Extension, byte[] Bytes)> Assets) : ClipboardContent;

    /// <summary>A bitmap from another app (a screenshot, an image copied in a browser…), as PNG bytes.</summary>
    public sealed record Image(byte[] Png) : ClipboardContent;

    /// <summary>Image files copied in Explorer.</summary>
    public sealed record Files(IReadOnlyList<string> Paths) : ClipboardContent;

    /// <summary>Plain text from anywhere.</summary>
    public sealed record Text(string Value) : ClipboardContent;
}

/// <summary>
/// Copy / Cut / Paste through the real Windows clipboard. Label Maker objects go on
/// as a private format (with the pixels of any images, so they paste into another
/// project intact) plus their plain text, so copying a label's text into Notepad or
/// Excel works too. Paste accepts Label Maker objects, bitmaps, image files and text
/// from any application.
/// </summary>
public static class ObjectClipboard
{
    private const string Format = "LabelMaker.Objects.v1";

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    /// <summary>True when there is anything paste could use. Cheap enough for command CanExecute.</summary>
    public static bool HasContent
    {
        get
        {
            try
            {
                return Clipboard.ContainsData(Format) || Clipboard.ContainsImage() ||
                       Clipboard.ContainsFileDropList() || Clipboard.ContainsText();
            }
            catch (COMException)
            {
                return false; // another app is holding the clipboard open
            }
        }
    }

    public static void Set(IEnumerable<LabelObject> objects, AssetStore assets)
    {
        List<LabelObject> items = objects.Select(o => o.Clone()).ToList();
        if (items.Count == 0)
        {
            return;
        }

        var payload = new Payload
        {
            Objects = items,
            Assets = items.OfType<ImageObject>()
                .Select(i => i.AssetId)
                .Distinct()
                .Select(id => assets.Get(id))
                .Where(a => a is not null)
                .ToDictionary(a => a!.AssetId, a => new PayloadAsset { Extension = a!.Extension, Data = Convert.ToBase64String(a.Bytes) }),
        };

        var data = new DataObject();
        data.SetData(Format, JsonSerializer.Serialize(payload, ProjectJson.Default));

        string text = string.Join(Environment.NewLine, items.SelectMany(o => o.GetSearchableStrings()).Where(s => !string.IsNullOrWhiteSpace(s)));
        if (text.Length > 0)
        {
            data.SetText(text);
        }

        Retry(() => Clipboard.SetDataObject(data, copy: true));
    }

    /// <summary>Reads the clipboard, preferring Label Maker objects, then images, files, then text.</summary>
    public static ClipboardContent? Read()
    {
        try
        {
            if (Clipboard.ContainsData(Format) && Clipboard.GetData(Format) is string json &&
                JsonSerializer.Deserialize<Payload>(json, ProjectJson.Default) is { } payload)
            {
                var assets = payload.Assets.ToDictionary(kv => kv.Key, kv => (kv.Value.Extension, Convert.FromBase64String(kv.Value.Data)));
                return new ClipboardContent.Objects(payload.Objects, assets);
            }

            if (Clipboard.ContainsFileDropList())
            {
                var images = Clipboard.GetFileDropList().Cast<string>()
                    .Where(p => ImageExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
                    .ToList();
                if (images.Count > 0)
                {
                    return new ClipboardContent.Files(images);
                }
            }

            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } bitmap)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                return new ClipboardContent.Image(ms.ToArray());
            }

            if (Clipboard.ContainsText() && Clipboard.GetText() is { Length: > 0 } text)
            {
                return new ClipboardContent.Text(text);
            }
        }
        catch (Exception ex) when (ex is COMException or JsonException or FormatException or ExternalException)
        {
            // Locked clipboard or a payload from an incompatible build: behave as if empty.
        }

        return null;
    }

    private static void Retry(Action action)
    {
        // The clipboard is a shared resource; another process briefly holding it
        // open is normal, so try a few times before giving up.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < 4)
            {
                Thread.Sleep(40);
            }
        }
    }

    private sealed class Payload
    {
        public List<LabelObject> Objects { get; set; } = new();

        public Dictionary<string, PayloadAsset> Assets { get; set; } = new();
    }

    private sealed class PayloadAsset
    {
        public string Extension { get; set; } = ".png";

        public string Data { get; set; } = string.Empty;
    }
}
