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
/// Maps a point in <b>page space</b> (mm, origin = physical page top-left, which is how
/// label templates are defined) to the coordinates drawn on the printed page. WPF print
/// pages (XPS) are positioned from the physical corner of the paper too, and the driver
/// clips whatever falls in its unprintable border, so no printer-margin offset is applied
/// here: subtracting it moved every label up and left by the printer's border (about
/// 4 mm on most office printers). Applies, in order: the fine scale correction and the
/// user scale-% about the page centre (default 100, never "fit to page"), then the
/// per-printer calibration offset.
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

    /// <summary>Map a page-space point (mm) to printed-page mm.</summary>
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
        return new PointMm(x + _calibration.OffsetXMm, y + _calibration.OffsetYMm);
    }

    public RectMm PageToCanvas(RectMm pageRect)
    {
        PointMm tl = PageToCanvas(pageRect.TopLeft);
        double sx = _calibration.ScaleX * _userScale;
        double sy = _calibration.ScaleY * _userScale;
        return new RectMm(tl.X, tl.Y, pageRect.Width * sx, pageRect.Height * sy);
    }

    /// <summary>True when a page-space rectangle, as printed, lands entirely within the printer's imageable area.</summary>
    public bool FitsImageableArea(RectMm pageRect)
    {
        RectMm printed = PageToCanvas(pageRect);
        return printed.Left >= _media.ImageableOriginXMm - 0.01 &&
               printed.Top >= _media.ImageableOriginYMm - 0.01 &&
               printed.Right <= _media.ImageableOriginXMm + _media.ImageableWidthMm + 0.01 &&
               printed.Bottom <= _media.ImageableOriginYMm + _media.ImageableHeightMm + 0.01;
    }

    /// <summary>Convenience: mm to device pixels at the target DPI.</summary>
    public static double MmToDevice(double mm, double dpi) => UnitConverter.MmToDevicePixels(mm, dpi);
}