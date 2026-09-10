using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Serialization;

namespace LabelMaker.App.Rendering;

/// <summary>Decoded WPF bitmap for the renderer, with cached brightness/contrast/flip variants.</summary>
public sealed class WpfDecodedImage : IDecodedImage
{
    private readonly BitmapSource _original;
    private readonly Dictionary<string, ImageSource> _variants = new();

    public WpfDecodedImage(BitmapSource source)
    {
        _original = source;
        PixelWidth = source.PixelWidth;
        PixelHeight = source.PixelHeight;
    }

    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public ImageSource ApplyAdjustments(ImageAdjustments adj)
    {
        string key = $"{adj.Brightness:0.###}|{adj.Contrast:0.###}|{adj.FlipHorizontal}|{adj.FlipVertical}|{adj.Grayscale}";
        if (_variants.TryGetValue(key, out ImageSource? cached))
        {
            return cached;
        }

        BitmapSource result = _original;

        if (adj.FlipHorizontal || adj.FlipVertical)
        {
            var transform = new ScaleTransform(adj.FlipHorizontal ? -1 : 1, adj.FlipVertical ? -1 : 1);
            result = new TransformedBitmap(result, transform);
        }

        // Brightness / contrast / grayscale need a pixel pass.
        if (adj.Grayscale || Math.Abs(adj.Brightness) > 1e-3 || Math.Abs(adj.Contrast) > 1e-3)
        {
            result = AdjustPixels(result, adj);
        }

        result.Freeze();
        _variants[key] = result;
        return result;
    }

    private static BitmapSource AdjustPixels(BitmapSource source, ImageAdjustments adj)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth;
        int h = converted.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[h * stride];
        converted.CopyPixels(pixels, stride, 0);

        double contrast = 1.0 + Math.Clamp(adj.Contrast, -1, 1);
        double brightness = Math.Clamp(adj.Brightness, -1, 1) * 255.0;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            double b = pixels[i];
            double g = pixels[i + 1];
            double r = pixels[i + 2];

            if (adj.Grayscale)
            {
                double lum = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
                r = g = b = lum;
            }

            r = ((r - 128) * contrast) + 128 + brightness;
            g = ((g - 128) * contrast) + 128 + brightness;
            b = ((b - 128) * contrast) + 128 + brightness;

            pixels[i] = (byte)Math.Clamp(b, 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(g, 0, 255);
            pixels[i + 2] = (byte)Math.Clamp(r, 0, 255);
        }

        var result = BitmapSource.Create(w, h, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}

/// <summary>Resolves project asset ids to decoded WPF bitmaps for the renderer.</summary>
public sealed class WpfImageSource : IImageSource
{
    private readonly AssetStore _assets;
    private readonly Dictionary<string, WpfDecodedImage?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public WpfImageSource(AssetStore assets) => _assets = assets;

    public void Invalidate() => _cache.Clear();

    public IDecodedImage? Get(string assetId)
    {
        if (string.IsNullOrEmpty(assetId))
        {
            return null;
        }

        if (_cache.TryGetValue(assetId, out WpfDecodedImage? cached))
        {
            return cached;
        }

        WpfDecodedImage? decoded = null;
        byte[]? bytes = _assets.GetBytes(assetId);
        if (bytes is not null)
        {
            try
            {
                using var ms = new MemoryStream(bytes, writable: false);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                decoded = new WpfDecodedImage(bmp);
            }
            catch (Exception)
            {
                decoded = null;
            }
        }

        _cache[assetId] = decoded;
        return decoded;
    }
}
