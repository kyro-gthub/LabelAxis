using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LabelMaker.App.Localization;

namespace LabelMaker.App;

/// <summary>
/// Every message box in LabelAxis goes through here: inside the LabelMaker.App namespaces this
/// class is found before <see cref="System.Windows.MessageBox"/>. In English it is the Windows
/// message box; in another language it is a LabelAxis one, because the Windows one would keep
/// its buttons in the language of Windows. The text, the title and the buttons are translated,
/// and the box is laid out right to left for Arabic and Kurdish.
/// </summary>
internal static class MessageBox
{
    public static MessageBoxResult Show(string text) =>
        Show(null, text, "LabelAxis", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string text, string caption, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None) =>
        Show(null, text, caption, button, icon);

    public static MessageBoxResult Show(Window? owner, string text, string caption = "LabelAxis",
        MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        owner ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        if (owner is { IsLoaded: false } or { IsVisible: false })
        {
            owner = null;
        }

        if (Localizer.Current.Code == "en")
        {
            return owner is null
                ? System.Windows.MessageBox.Show(text, caption, button, icon)
                : System.Windows.MessageBox.Show(owner, text, caption, button, icon);
        }

        var box = new MessageDialog(Localizer.T(text), Localizer.T(caption), button, icon);
        if (owner is not null)
        {
            box.Owner = owner;
            box.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            box.ShowInTaskbar = false;
        }

        box.ShowDialog();
        return box.Result;
    }

    private sealed class MessageDialog : Window
    {
        private readonly string _text;

        public MessageDialog(string text, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            _text = text;
            Style = (Style)Application.Current.FindResource("AppWindow");
            Title = caption;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            MinWidth = 340;

            Result = buttons switch
            {
                MessageBoxButton.YesNo => MessageBoxResult.No,
                MessageBoxButton.OK => MessageBoxResult.OK,
                _ => MessageBoxResult.Cancel,
            };

            var message = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440,
                VerticalAlignment = VerticalAlignment.Center,
                LineHeight = 20,
            };
            Localizer.SetOff(message, true);

            var body = new DockPanel { Margin = new Thickness(22, 20, 22, 18) };
            if (Glyph(icon) is { } glyph)
            {
                DockPanel.SetDock(glyph, Dock.Left);
                glyph.Margin = new Thickness(0, 0, 16, 0);
                body.Children.Add(glyph);
            }

            body.Children.Add(message);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            foreach ((string label, MessageBoxResult result, bool isDefault, bool isCancel) in Buttons(buttons))
            {
                var b = new Button
                {
                    Content = Localizer.T(label),
                    MinWidth = 88,
                    Padding = new Thickness(14, 5, 14, 5),
                    Margin = new Thickness(8, 0, 0, 0),
                    IsDefault = isDefault,
                    IsCancel = isCancel,
                };
                if (isDefault && Application.Current.TryFindResource("AccentButton") is Style accent)
                {
                    b.Style = accent;
                }

                b.Click += (_, _) =>
                {
                    Result = result;
                    DialogResult = true;
                };
                row.Children.Add(b);
            }

            var footer = new Border
            {
                Padding = new Thickness(22, 12, 22, 12),
                Child = row,
            };
            footer.SetResourceReference(Border.BackgroundProperty, "Br.Surface");

            var root = new DockPanel();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            root.Children.Add(body);
            Content = root;

            // Ctrl+C copies the message, as it does in the Windows message box.
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    try
                    {
                        Clipboard.SetText(caption + Environment.NewLine + _text);
                    }
                    catch (System.Runtime.InteropServices.ExternalException)
                    {
                        // The clipboard is busy; nothing to do.
                    }
                }
            };

            Loaded += (_, _) => Sound(icon);
        }

        public MessageBoxResult Result { get; private set; }

        private static IEnumerable<(string, MessageBoxResult, bool, bool)> Buttons(MessageBoxButton buttons) => buttons switch
        {
            MessageBoxButton.OKCancel => new[] { ("OK", MessageBoxResult.OK, true, false), ("Cancel", MessageBoxResult.Cancel, false, true) },
            MessageBoxButton.YesNo => new[] { ("Yes", MessageBoxResult.Yes, true, false), ("No", MessageBoxResult.No, false, true) },
            MessageBoxButton.YesNoCancel => new[]
            {
                ("Yes", MessageBoxResult.Yes, true, false), ("No", MessageBoxResult.No, false, false), ("Cancel", MessageBoxResult.Cancel, false, true),
            },
            _ => new[] { ("OK", MessageBoxResult.OK, true, true) },
        };

        private static FrameworkElement? Glyph(MessageBoxImage icon)
        {
            (string mark, string brush)? look = icon switch
            {
                MessageBoxImage.Information => ("i", "Br.Accent"),
                MessageBoxImage.Question => ("?", "Br.Accent"),
                MessageBoxImage.Warning => ("!", "Br.Warning"),
                MessageBoxImage.Error => ("×", "Br.Danger"),
                _ => null,
            };
            if (look is null)
            {
                return null;
            }

            var circle = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = look.Value.mark,
                    Foreground = Brushes.White,
                    FontSize = 19,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FlowDirection = FlowDirection.LeftToRight,
                },
            };
            if (Application.Current.TryFindResource(look.Value.brush) is Brush)
            {
                circle.SetResourceReference(Border.BackgroundProperty, look.Value.brush);
            }
            else
            {
                circle.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xA3, 0x17));
            }

            return circle;
        }

        private static void Sound(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Error:
                    System.Media.SystemSounds.Hand.Play();
                    break;
                case MessageBoxImage.Warning:
                    System.Media.SystemSounds.Exclamation.Play();
                    break;
                case MessageBoxImage.Question:
                    System.Media.SystemSounds.Question.Play();
                    break;
                case MessageBoxImage.Information:
                    System.Media.SystemSounds.Asterisk.Play();
                    break;
            }
        }
    }
}
