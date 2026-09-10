using LabelMaker.Core.Rendering;
using LabelMaker.Core.Serialization;
using LabelMaker.Export.Svg;

namespace LabelMaker.Export;

/// <summary>
/// Bridges the project <see cref="AssetStore"/> to the renderer's
/// <see cref="IImageSource"/> for headless export. Images are exposed to SVG as
/// base64 data URIs; other surfaces get raw pixel dimensions only.
/// </summary>
public sealed class ExportImageSource : IImageSource
{
    private readonly AssetStore _assets;
    private readonly Dictionary<string, IDecodedImage?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ExportImageSource(AssetStore assets) => _assets = assets;

    public IDecodedImage? Get(string assetId)
    {
        if (string.IsNullOrEmpty(assetId))
        {
            return null;
        }

        if (_cache.TryGetValue(assetId, out IDecodedImage? cached))
        {
            return cached;
        }

        ProjectAsset? asset = _assets.Get(assetId);
        IDecodedImage? result = null;
        if (asset is not null)
        {
            (int w, int h) = ImageDimensions.Probe(asset.Bytes);
            string mime = MimeFor(asset.Extension);
            result = new SvgEmbeddedImage
            {
                PixelWidth = w > 0 ? w : 1,
                PixelHeight = h > 0 ? h : 1,
                DataUri = $"data:{mime};base64,{Convert.ToBase64String(asset.Bytes)}",
            };
        }

        _cache[assetId] = result;
        return result;
    }

    private static string MimeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".bmp" => "image/bmp",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".tif" or ".tiff" => "image/tiff",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };
}
