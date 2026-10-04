using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Settings;
using LabelMaker.Core.Units;

namespace LabelMaker.App.Controls;

/// <summary>Zoom, panning, the millimetre-to-screen transform and the scroll bars.</summary>
public sealed partial class DesignerCanvas
{
    // ---- view transform ---------------------------------------------------

    private double RulerPad => (Settings?.ShowRulers ?? true) ? RulerSize : 0;

    private double LabelWidthMm => _session?.ActiveLabel.SizeMm.Width ?? 100;
    private double LabelHeightMm => _session?.ActiveLabel.SizeMm.Height ?? 60;

    private double Scale => _zoom * K;

    /// <summary>Screen DIU per document millimetre at the current zoom.</summary>
    public double ScreenScale => Scale;

    /// <summary>An object's un-rotated box in this control's coordinates (for overlays such as the text editor).</summary>
    public Rect ObjectScreenRect(LabelObject obj)
    {
        Point a = MmToScreen(obj.Bounds.X, obj.Bounds.Y);
        Point b = MmToScreen(obj.Bounds.Right, obj.Bounds.Bottom);
        return new Rect(a, b);
    }

    /// <summary>Raised on double-click (or Enter / F2) on a text object.</summary>
    public event EventHandler<LabelObject>? EditTextRequested;

    /// <summary>Also raised when the view pans, so overlays can follow or close.</summary>
    public event EventHandler? ViewChanged;

    private string? _hiddenObjectId;

    /// <summary>An object left out of the rendering — the one being edited in place, so it isn't drawn twice.</summary>
    public string? HiddenObjectId
    {
        get => _hiddenObjectId;
        set
        {
            _hiddenObjectId = value;
            InvalidateVisual();
        }
    }

    private Point ContentOrigin
    {
        get
        {
            double pad = RulerPad;
            return new Point(
                pad + ((ActualWidth - pad - (LabelWidthMm * Scale)) / 2.0) + _pan.X,
                pad + ((ActualHeight - pad - (LabelHeightMm * Scale)) / 2.0) + _pan.Y);
        }
    }

    private Point ScreenToMm(Point p)
    {
        Point o = ContentOrigin;
        return new Point((p.X - o.X) / Scale, (p.Y - o.Y) / Scale);
    }

    private Point MmToScreen(double xMm, double yMm)
    {
        Point o = ContentOrigin;
        return new Point(o.X + (xMm * Scale), o.Y + (yMm * Scale));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (!_viewInitialised && ActualWidth > 0 && ActualHeight > 0)
        {
            FitToView();
        }
    }

    public void FitToView()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        double pad = RulerPad;
        const double margin = 48;
        double sx = (ActualWidth - pad - (margin * 2)) / (LabelWidthMm * K);
        double sy = (ActualHeight - pad - (margin * 2)) / (LabelHeightMm * K);
        _zoom = Math.Clamp(Math.Min(sx, sy), MinZoom, MaxZoom);
        _pan = default;
        _viewInitialised = true;
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomToActualSize()
    {
        double screenDpi = Settings?.ScreenDpiCalibration ?? UnitConverter.DiuPerInch;
        ZoomToward(Math.Clamp(screenDpi / UnitConverter.DiuPerInch, MinZoom, MaxZoom), Centre);
    }

    public void ZoomIn() => ZoomAt(1.2, Centre);

    public void ZoomOut() => ZoomAt(1 / 1.2, Centre);

    private Point Centre => new(ActualWidth / 2.0, ActualHeight / 2.0);

    private void ZoomAt(double factor, Point anchor) =>
        ZoomToward(Math.Clamp(_zoom * factor, MinZoom, MaxZoom), anchor);

    private void ZoomToward(double newZoom, Point anchorScreen)
    {
        if (Math.Abs(newZoom - _zoom) < 1e-6)
        {
            return;
        }

        Point mm = ScreenToMm(anchorScreen);
        _zoom = newZoom;
        double pad = RulerPad;
        _pan = new Vector(
            anchorScreen.X - (mm.X * Scale) - pad - ((ActualWidth - pad - (LabelWidthMm * Scale)) / 2.0),
            anchorScreen.Y - (mm.Y * Scale) - pad - ((ActualHeight - pad - (LabelHeightMm * Scale)) / 2.0));
        _viewInitialised = true;
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- scrolling --------------------------------------------------------

    /// <summary>One scroll bar's range, position and page size, in screen pixels.</summary>
    public readonly record struct ScrollInfo(double Minimum, double Maximum, double Value, double Viewport);

    /// <summary>Raised when the scroll ranges or positions change (zoom, pan, resize, another page).</summary>
    public event EventHandler? ScrollChanged;

    private ScrollInfo _lastHorizontal;
    private ScrollInfo _lastVertical;

    public ScrollInfo HorizontalScroll => Axis(ActualWidth - RulerPad, LabelWidthMm * Scale, _pan.X);

    public ScrollInfo VerticalScroll => Axis(ActualHeight - RulerPad, LabelHeightMm * Scale, _pan.Y);

    /// <summary>
    /// The label can be scrolled until half a window of empty space shows past either edge,
    /// whether it is smaller or larger than the view. A position reached by dragging the
    /// canvas further than that widens the range instead of snapping back.
    /// </summary>
    private static ScrollInfo Axis(double view, double content, double pan)
    {
        view = Math.Max(1, view);
        double half = Math.Max(0, (content - view) / 2) + (view / 2);
        half = Math.Max(half, Math.Abs(pan));
        return new ScrollInfo(-half, half, -pan, view);
    }

    /// <summary>Moves the view to a scroll bar position.</summary>
    public void ScrollTo(bool horizontal, double value)
    {
        _pan = horizontal ? new Vector(-value, _pan.Y) : new Vector(_pan.X, -value);
        _viewInitialised = true;
        ViewChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void RaiseScrollChanged()
    {
        ScrollInfo h = HorizontalScroll, v = VerticalScroll;
        if (h != _lastHorizontal || v != _lastVertical)
        {
            _lastHorizontal = h;
            _lastVertical = v;
            ScrollChanged?.Invoke(this, EventArgs.Empty);
        }
    }

}
