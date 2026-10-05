using System.Windows;
using LabelMaker.Core.Designs;
using LabelMaker.Core.Formtec;

namespace LabelMaker.App.Views;

/// <summary>"Start from a design": the design gallery on its own, with Use this design.</summary>
public partial class DesignGalleryWindow : Window
{
    public DesignGalleryWindow(TemplateStore store)
    {
        InitializeComponent();
        Gallery.Store = store;
        Gallery.SelectionChanged += (_, _) => UseButton.IsEnabled = Gallery.SelectedDesign is not null;
        Gallery.DesignActivated += (_, _) => OnUse(this, new RoutedEventArgs());
        UseButton.IsEnabled = Gallery.SelectedDesign is not null;
    }

    /// <summary>The design chosen, when the window closes with "Use this design".</summary>
    public LabelDesign? ChosenDesign { get; private set; }

    /// <summary>Keep {Product}, {Price}… as data fields instead of writing the sample words in.</summary>
    public bool UseDataFields => Gallery.UseDataFields;

    private void OnUse(object sender, RoutedEventArgs e)
    {
        if (Gallery.SelectedDesign is { } design)
        {
            ChosenDesign = design;
            DialogResult = true;
        }
    }
}
