using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Controls;

/// <summary>A rounded gradient track with a round thumb that shows the colour at that point.</summary>
public sealed class PillSlider
{
    private readonly Grid _host;
    private readonly Border _track = new() { CornerRadius = new CornerRadius(8) };
    private readonly Canvas _layer = new() { ClipToBounds = false };
    private readonly Ellipse _thumb = new()
    {
        Width = 20,
        Height = 20,
        Stroke = Brushes.White,
        StrokeThickness = 3,
        Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 0, Opacity = 0.55 },
        IsHitTestVisible = false,
    };

    private readonly Action<double> _changed;
    private double _fraction;
    private bool _dragging;

    public PillSlider(Grid host, bool checkered, Action<double> changed)
    {
        _host = host;
        _changed = changed;
        var frame = new Border { CornerRadius = new CornerRadius(8), Background = checkered ? ColourBrushes.Checker(8) : null, Child = _track };
        host.Children.Add(frame);
        host.Children.Add(_layer);
        _layer.Children.Add(_thumb);
        host.Background = Brushes.Transparent;
        host.Cursor = Cursors.Hand;
        host.MouseLeftButtonDown += (_, e) => { _dragging = true; host.CaptureMouse(); Pick(e.GetPosition(host)); };
        host.MouseMove += (_, e) => { if (_dragging) { Pick(e.GetPosition(host)); } };
        host.MouseLeftButtonUp += (_, _) => { _dragging = false; host.ReleaseMouseCapture(); };
        host.SizeChanged += (_, _) => Place();
    }

    public void Background(ColorRgba[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int i = 0; i < stops.Length; i++)
        {
            brush.GradientStops.Add(new GradientStop(ColourBrushes.ToWpf(stops[i]), i / (double)Math.Max(1, stops.Length - 1)));
        }

        _track.Background = brush;
    }

    public void Set(double fraction, ColorRgba colour)
    {
        _fraction = Math.Clamp(fraction, 0, 1);
        _thumb.Fill = new SolidColorBrush(ColourBrushes.ToWpf(colour with { A = 255 }));
        Place();
    }

    private void Place()
    {
        // The thumb stays within the track's rounded ends.
        double usable = Math.Max(0, _host.ActualWidth - 16);
        Canvas.SetLeft(_thumb, 8 + (_fraction * usable) - 10);
        Canvas.SetTop(_thumb, (_host.ActualHeight / 2) - 10);
    }

    private void Pick(Point p) => _changed(Math.Clamp((p.X - 8) / Math.Max(1, _host.ActualWidth - 16), 0, 1));
}
