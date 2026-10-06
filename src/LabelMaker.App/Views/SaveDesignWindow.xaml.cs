using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LabelMaker.App.Localization;

namespace LabelMaker.App.Views;

/// <summary>
/// Names a page being saved as one of the user's own designs, or renames one. Says so when a
/// design of that name already exists (saving then replaces it).
/// </summary>
public partial class SaveDesignWindow : Window
{
    private readonly Func<string, bool>? _exists;

    /// <param name="exists">Whether a design of a name is already saved (for the "replaces it" note).</param>
    public SaveDesignWindow(string name, string? description, ImageSource? preview, bool renaming = false, Func<string, bool>? exists = null)
    {
        InitializeComponent();
        _exists = exists;
        PreviewImage.Source = preview;
        NameBox.Text = name;
        DescriptionBox.Text = description ?? string.Empty;
        if (renaming)
        {
            Title = Localizer.T("Rename design");
            SaveButton.Content = Localizer.T("Rename");
            IntroText.Visibility = Visibility.Collapsed;
        }

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
        OnNameChanged(NameBox, null!);
    }

    public string DesignName => NameBox.Text.Trim();

    public string? DesignDescription => DescriptionBox.Text.Trim() is { Length: > 0 } d ? d : null;

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        string name = DesignName;
        SaveButton.IsEnabled = name.Length > 0;
        bool taken = name.Length > 0 && _exists?.Invoke(name) == true;
        NoteText.Text = name.Length == 0 ? Localizer.T("Give the design a name.")
            : taken ? Localizer.T("A design with this name is already saved: saving replaces it.")
            : string.Empty;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (DesignName.Length > 0)
        {
            DialogResult = true;
        }
    }
}
