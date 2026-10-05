using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Rendering;

/// <summary>
/// Typefaces and brushes shared by text measurement and drawing. Building a
/// <see cref="FontFamily"/> parses the family name and resolves the font every
/// time, and the renderer used to do that for every word of every label — on a
/// 10,000-record job that was most of the time spent per sheet. Everything here is
/// immutable (brushes are frozen), so the caches are safe from any thread.
/// </summary>
internal static class WpfFontCache
{
    private static readonly ConcurrentDictionary<(string Family, bool Bold, bool Italic), Typeface> Typefaces = new();
    private static readonly ConcurrentDictionary<ColorRgba, SolidColorBrush> Brushes = new();

    public static Typeface Typeface(string family, bool bold, bool italic) =>
        Typefaces.GetOrAdd((family, bold, italic), static key => new Typeface(
            BundledFonts.Resolve(key.Family),
            key.Italic ? FontStyles.Italic : FontStyles.Normal,
            key.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal));

    /// <summary>Forgets the typefaces, after a font is added or removed (a name may now mean another font).</summary>
    public static void Clear()
    {
        Typefaces.Clear();
        Interlocked.Increment(ref _generation);
    }

    private static int _generation;

    /// <summary>Goes up each time the fonts change, so measurements made with the old fonts can be thrown away.</summary>
    public static int Generation => Volatile.Read(ref _generation);

    public static SolidColorBrush Brush(ColorRgba c) =>
        Brushes.GetOrAdd(c, static color =>
        {
            var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        });
}
