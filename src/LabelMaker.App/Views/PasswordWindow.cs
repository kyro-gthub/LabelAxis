using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LabelMaker.App.Views;

/// <summary>Masked password prompt in the app's style (built in code, like <see cref="InputWindow"/>).</summary>
public sealed class PasswordWindow : Window
{
    private readonly PasswordBox _box;

    public PasswordWindow(string title, string message)
    {
        Style = (Style)Application.Current.FindResource("AppWindow");
        SetResourceReference(BackgroundProperty, "Br.Surface");
        Title = title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _box = new PasswordBox { Margin = new Thickness(0, 8, 0, 0), MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };

        var ok = new Button { Content = "Connect", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "PrimaryButton");
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        cancel.SetResourceReference(StyleProperty, "GhostButton");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        ok.Margin = new Thickness(8, 0, 0, 0);

        var note = new TextBlock
        {
            Text = "Used for this session only. To remember it, edit the connection and tick “Remember password”.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Margin = new Thickness(0, 8, 0, 0),
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Br.TextMuted");

        var label = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Br.Text");

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(label);
        panel.Children.Add(_box);
        panel.Children.Add(note);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) => _box.Focus();
    }

    public string Password => _box.Password;
}