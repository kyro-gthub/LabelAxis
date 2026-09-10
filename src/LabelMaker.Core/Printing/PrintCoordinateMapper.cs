using LabelMaker.Core.Units;

namespace LabelMaker.Core.Printing;

/// <summary>
/// Physical geometry of the target sheet as reported by the print system.
/// All values are millimetres with the origin at the sheet's physical top-left.
/// </summary>
public sealed record SheetMedia
{
    public required double PageWidthMm { get; init; }
    public required double PageHeightMm { get; init; }

    /// <summary>Left edge of the printer's imageable (printable) area.</summary>
    public double ImageableOriginXMm { get; init; }

    /// <summary>Top edge of the printer's imageable (printable) area.</summary>
    public double ImageableOriginYMm { get; init; }

    public double ImageableWidthMm { get; init; }
    public double ImageableHeightMm { get; init; }

    public static SheetMedia FullBleed(double widthMm, double heightMm) => new()
    {
        PageWidthMm = widthMm,
        PageHeightMm = heightMm,
        ImageableWidthMm = widthMm,
        ImageableHeightMm = heightMm,
    };
}

/// <summary>
/// Maps a point in <b>page space</b> (mm, origin = physical page top-left, which
/// is how Formtec templates are defined) to <b>canvas space</b> that the drawing
/// surface uses when it has already been translated to the printer's imageable
/// origin. Applies, in order: subtract the imageable origin, add the per-printer
/// calibration offset, apply the fine scale correction about the page centre, and
/// apply the user scale-% (default 100, i.e. no change — never "fit to page",
/// spec §16).
/// </summary>
public sealed class PrintCoordinateMapper
{
    private readonly SheetMedia _media;
    private readonly PrinterCalibration _calibration;
    private readonly double _userScale;

    public PrintCoordinateMapper(SheetMedia media, PrinterCalibration? calibration = null, double userScalePercent = 100.0)
    {
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _calibration = calibration ?? PrinterCalibration.Identity(string.Empty);
        _userScale = userScalePercent <= 0 ? 1.0 : userScalePercent / 100.0;
    }

    public double UserScale => _userScale;

    /// <summary>Map a page-space point (mm) to imageable-origin-relative canvas mm.</summary>
    public PointMm PageToCanvas(PointMm pagePoint)
    {
        double cx = _media.PageWidthMm / 2.0;
        double cy = _media.PageHeightMm / 2.0;

        // Fine scale + user scale about the page centre.
        double sx = _calibration.ScaleX * _userScale;
        double sy = _calibration.ScaleY * _userScale;
        double x = cx + ((pagePoint.X - cx) * sx);
        double y = cy + ((pagePoint.Y - cy) * sy);

        // Calibration translation (wizard result).
        x += _calibration.OffsetXMm;
        y += _calibration.OffsetYMm;

        // Move into the surface's imageable-origin coordinate space.
        x -= _media.ImageableOriginXMm;
        y -= _media.ImageableOriginYMm;

        return new PointMm(x, y);
    }

    public RectMm PageToCanvas(RectMm pageRect)
    {
        PointMm tl = PageToCanvas(pageRect.TopLeft);
        double sx = _calibration.ScaleX * _userScale;
        double sy = _calibration.ScaleY * _userScale;
        return new RectMm(tl.X, tl.Y, pageRect.Width * sx, pageRect.Height * sy);
    }

    /// <summary>True when a page-space rectangle lands entirely within the imageable area.</summary>
    public bool FitsImageableArea(RectMm pageRect)
    {
        RectMm canvas = PageToCanvas(pageRect);
        return canvas.Left >= -0.01 &&
               canvas.Top >= -0.01 &&
               canvas.Right <= _media.ImageableWidthMm + 0.01 &&
               canvas.Bottom <= _media.ImageableHeightMm + 0.01;
    }

    /// <summary>Convenience: canvas-space mm to device pixels at the target DPI.</summary>
    public static double MmToDevice(double mm, double dpi) => UnitConverter.MmToDevicePixels(mm, dpi);
}
