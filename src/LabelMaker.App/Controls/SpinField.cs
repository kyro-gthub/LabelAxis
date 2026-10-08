using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LabelMaker.App.Controls;

/// <summary>
/// A number with small up / down arrows and its name underneath. Wheel and arrow keys step it;
/// Shift steps by 10. Nothing is typed back in while <c>busy</c> says the owner is filling the
/// boxes in itself.
/// </summary>
public sealed class SpinField
{
    private readonly Func<bool> _busy;
    private readonly Func<string> _caption;
    private readonly Func<double> _max;
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private readonly TextBox _box = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0, 0, 0),
    };

    private readonly TextBlock _label = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };

    public SpinField(Func<bool> busy, Func<string> caption, Func<double> max, Func<double> get, Action<double> set)
    {
        _busy = busy;
        _caption = caption;
        _max = max;
        _get = get;
        _set = set;
    }

    public UIElement Build(bool last)
    {
        _box.SetResourceReference(Control.StyleProperty, "PlainTextBox");
        _label.SetResourceReference(TextBlock.ForegroundProperty, "Br.TextMuted");

        var arrows = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) };
        arrows.Children.Add(Arrow("M 0,4 L 4,0 L 8,4", 1));
        arrows.Children.Add(Arrow("M 0,0 L 4,4 L 8,0", -1));

        var inner = new DockPanel { Height = 34 };
        DockPanel.SetDock(arrows, Dock.Right);
        inner.Children.Add(arrows);
        inner.Children.Add(_box);

        var frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = inner,
        };
        frame.SetResourceReference(Border.BackgroundProperty, "Br.Surface");
        frame.SetResourceReference(Border.BorderBrushProperty, "Br.Border");
        _box.GotKeyboardFocus += (_, _) =>
        {
            frame.SetResourceReference(Border.BorderBrushProperty, "Br.Accent");
            _box.SelectAll();
        };
        _box.LostKeyboardFocus += (_, _) =>
        {
            frame.SetResourceReference(Border.BorderBrushProperty, "Br.Border");
            Commit();
        };
        _box.PreviewKeyDown += (_, e) =>
        {
            double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            switch (e.Key)
            {
                case Key.Enter:
                    Commit();
                    _box.SelectAll();
                    e.Handled = true;
                    break;
                case Key.Up:
                    Step(step);
                    e.Handled = true;
                    break;
                case Key.Down:
                    Step(-step);
                    e.Handled = true;
                    break;
            }
        };
        // Preview: the text box's scroll viewer marks the wheel handled before it bubbles up.
        frame.PreviewMouseWheel += (_, e) =>
        {
            Step((e.Delta > 0 ? 1 : -1) * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1));
            e.Handled = true;
        };

        var column = new StackPanel { Margin = new Thickness(0, 0, last ? 0 : 8, 0) };
        column.Children.Add(frame);
        column.Children.Add(_label);
        return column;
    }

    public void Refresh()
    {
        _label.Text = _caption();
        if (!_box.IsKeyboardFocusWithin)
        {
            _box.Text = Math.Round(_get()).ToString(CultureInfo.CurrentCulture);
        }
    }

    private RepeatButton Arrow(string glyph, int direction)
    {
        var path = new Path { Data = Geometry.Parse(glyph), StrokeThickness = 1.4, Margin = new Thickness(0, 2, 0, 2) };
        path.SetResourceReference(Shape.StrokeProperty, "Br.TextMuted");
        var button = new RepeatButton
        {
            Width = 16,
            Height = 14,
            Focusable = false,
            Cursor = Cursors.Hand,
            Delay = 350,
            Interval = 70,
            Template = ArrowTemplate(),
            Content = path,
        };
        button.Click += (_, _) => Step(direction);
        return button;
    }

    private static ControlTemplate ArrowTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(RepeatButton)) { VisualTree = border };
    }

    /// <summary>
    /// Arrows, wheel and arrow keys. The box is refreshed even while it has focus: it used to
    /// keep showing the old number, and leaving the box then typed that number back in.
    /// </summary>
    private void Step(double amount)
    {
        _set(Math.Clamp(Math.Round(_get()) + amount, 0, _max()));
        _box.Text = Math.Round(_get()).ToString(CultureInfo.CurrentCulture);
        if (_box.IsKeyboardFocusWithin)
        {
            _box.SelectAll();
        }
    }

    private void Commit()
    {
        if (_busy())
        {
            return;
        }

        if (double.TryParse(_box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
            double.TryParse(_box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            _set(Math.Clamp(Math.Round(value), 0, _max()));
        }
        else
        {
            Refresh();
        }
    }
}
