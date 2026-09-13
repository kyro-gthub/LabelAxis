using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabelMaker.App.Mvvm;
using LabelMaker.App.ViewModels;
using LabelMaker.Data.Publisher;
using Microsoft.Win32;

namespace LabelMaker.App.Views;

/// <summary>
/// Shows what was read from a Publisher file — each text box with its paragraphs, and
/// every picture — and lets the user tick what to bring in: as text boxes, as records
/// for variable-data printing, or as pictures on the label.
/// </summary>
public partial class PublisherImportWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly string _path;
    private readonly ObservableCollection<StoryItem> _stories;
    private readonly ObservableCollection<PictureItem> _pictures;

    public PublisherImportWindow(MainViewModel vm, PublisherContent content, string path)
    {
        _vm = vm;
        _path = path;
        InitializeComponent();

        _stories = new ObservableCollection<StoryItem>(content.Stories.Select(s => new StoryItem(s)));
        _pictures = new ObservableCollection<PictureItem>(content.Images.Select(i => new PictureItem(i)));
        StoryList.ItemsSource = _stories;
        PictureList.ItemsSource = _pictures;

        int paragraphs = content.Stories.Sum(s => s.Paragraphs.Count);
        HeaderTitle.Text = "Import from " + Path.GetFileName(path);
        HeaderSubtitle.Text = $"{content.Stories.Count} text box(es) · {paragraphs} paragraph(s) · {content.Images.Count} picture(s)";
        TextHeader.Text = $"TEXT ({paragraphs})";
        PictureHeader.Text = $"PICTURES ({content.Images.Count})";
        NoPictures.Visibility = content.Images.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (content.Notes.Count > 0)
        {
            NoteBar.Visibility = Visibility.Visible;
            NoteText.Text = string.Join("  ", content.Notes);
        }
    }

    // ---- selection ------------------------------------------------------------------

    private void OnAllText(object sender, RoutedEventArgs e) => SetAllText(true);

    private void OnNoText(object sender, RoutedEventArgs e) => SetAllText(false);

    private void SetAllText(bool value)
    {
        foreach (StoryItem story in _stories)
        {
            story.IsChecked = value;
        }
    }

    private void OnAllPictures(object sender, RoutedEventArgs e) => SetAllPictures(true);

    private void OnNoPictures(object sender, RoutedEventArgs e) => SetAllPictures(false);

    private void SetAllPictures(bool value)
    {
        foreach (PictureItem picture in _pictures)
        {
            picture.IsChecked = value;
        }
    }

    // ---- actions ----------------------------------------------------------------------

    private void OnAddText(object sender, RoutedEventArgs e)
    {
        List<string> texts = _stories
            .Select(s => string.Join('\n', s.Paragraphs.Where(p => p.IsChecked).Select(p => p.Text)))
            .Where(t => t.Length > 0)
            .ToList();
        if (texts.Count == 0)
        {
            Say("Tick some text first.");
            return;
        }

        int added = _vm.AddTextBoxes(texts);
        Say(added == 1 ? "Added 1 text box to the label." : $"Added {added} text boxes to the label.");
    }

    private void OnUseRecords(object sender, RoutedEventArgs e)
    {
        List<string> paragraphs = _stories.SelectMany(s => s.Paragraphs).Where(p => p.IsChecked).Select(p => p.Text).ToList();
        if (paragraphs.Count == 0)
        {
            Say("Tick the paragraphs to use as records.");
            return;
        }

        int records = _vm.UseParagraphsAsRecords(paragraphs, SplitTabsBox.IsChecked == true, HeaderRowBox.IsChecked == true, _path);
        Say(records == 0
            ? "No records were made (a header row needs at least one more paragraph)."
            : $"{records:N0} record(s) are now the project's data. Add fields to the label from the Data panel.");
    }

    private void OnAddPictures(object sender, RoutedEventArgs e)
    {
        List<PublisherImage> images = _pictures.Where(p => p.IsChecked).Select(p => p.Image).ToList();
        if (images.Count == 0)
        {
            Say("Tick some pictures first.");
            return;
        }

        int added = _vm.AddPictures(images, Path.GetFileName(_path));
        Say(added == 1 ? "Added 1 picture to the label." : $"Added {added} pictures to the label.");
    }

    private void OnSavePictures(object sender, RoutedEventArgs e)
    {
        List<PublisherImage> images = _pictures.Where(p => p.IsChecked).Select(p => p.Image).ToList();
        if (images.Count == 0)
        {
            Say("Tick some pictures first.");
            return;
        }

        var dialog = new OpenFolderDialog { Title = "Save pictures to folder" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string stem = Path.GetFileNameWithoutExtension(_path);
        try
        {
            foreach (PublisherImage image in images)
            {
                string file = Path.Combine(dialog.FolderName, $"{stem} picture {image.Number}.{image.Extension}");
                File.WriteAllBytes(file, image.Bytes);
            }

            Say($"Saved {images.Count} picture(s) to {dialog.FolderName}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("Could not save: " + ex.Message);
        }
    }

    private void Say(string message) => StatusText.Text = message;

    // ---- items --------------------------------------------------------------------------

    private sealed class StoryItem : ObservableObject
    {
        private bool _updating;

        public StoryItem(PublisherStory story)
        {
            Title = $"Text box {story.Number}";
            Summary = story.Paragraphs.Count == 1 ? " · 1 paragraph" : $" · {story.Paragraphs.Count} paragraphs";
            Paragraphs = story.Paragraphs.Select(p => new ParagraphItem(p, this)).ToList();
        }

        public string Title { get; }

        public string Summary { get; }

        public IReadOnlyList<ParagraphItem> Paragraphs { get; }

        public bool IsChecked
        {
            get => Paragraphs.All(p => p.IsChecked);
            set
            {
                _updating = true;
                foreach (ParagraphItem p in Paragraphs)
                {
                    p.IsChecked = value;
                }

                _updating = false;
                OnPropertyChanged();
            }
        }

        public void ChildChanged()
        {
            if (!_updating)
            {
                OnPropertyChanged(nameof(IsChecked));
            }
        }
    }

    private sealed class ParagraphItem : ObservableObject
    {
        private readonly StoryItem _owner;
        private bool _isChecked = true;

        public ParagraphItem(string text, StoryItem owner)
        {
            Text = text;
            _owner = owner;
            Display = text.Replace('\t', '→').Replace("\n", " ↵ ", StringComparison.Ordinal);
        }

        public string Text { get; }

        public string Display { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (SetField(ref _isChecked, value))
                {
                    _owner.ChildChanged();
                }
            }
        }
    }

    private sealed class PictureItem : ObservableObject
    {
        private bool _isChecked = true;

        public PictureItem(PublisherImage image)
        {
            Image = image;
            Caption = $"{image.Extension.ToUpperInvariant()} · {image.Width} × {image.Height}";
            Thumbnail = MakeThumbnail(image.Bytes);
        }

        public PublisherImage Image { get; }

        public string Caption { get; }

        public ImageSource? Thumbnail { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set => SetField(ref _isChecked, value);
        }

        private static ImageSource? MakeThumbnail(byte[] bytes)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 260;
                bitmap.StreamSource = new MemoryStream(bytes);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
            {
                // Listed without a preview; the bytes can still be saved or placed.
                return null;
            }
        }
    }
}
