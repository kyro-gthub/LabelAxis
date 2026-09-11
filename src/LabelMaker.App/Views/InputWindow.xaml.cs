using System.Windows;
using System.Windows.Controls;

namespace LabelMaker.App.Views;

/// <summary>Minimal single-line text prompt (built in code — no XAML).</summary>
public sealed class InputWindow : Window
{
    private readonly TextBox _box;

    public InputWindow(string title, string label, string initialValue)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _box = new TextBox { Text = initialValue, Margin = new Thickness(0, 6, 0, 0) };
        _box.SelectAll();

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 74, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { DialogResult = true; };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 74 };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(_box);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) => _box.Focus();
    }

    public string Value => _box.Text;
}
