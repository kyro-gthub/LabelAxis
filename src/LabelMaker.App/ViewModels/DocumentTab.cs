using System.IO;
using LabelMaker.App.Controls;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;

namespace LabelMaker.App.ViewModels;

/// <summary>
/// One open project in the tab strip: its session (document, undo history, records),
/// the image cache and property panel bound to it, and the bits of view state that
/// should come back when the tab is shown again.
/// </summary>
public sealed class DocumentTab : ObservableObject
{
    private bool _isActive;

    internal DocumentTab(DocumentSession session, WpfImageSource images, SelectionProperties properties)
    {
        Session = session;
        Images = images;
        Properties = properties;
    }

    public DocumentSession Session { get; }

    internal WpfImageSource Images { get; }

    internal SelectionProperties Properties { get; }

    /// <summary>The user has chosen print settings for this project in this session (don't guess them any more).</summary>
    internal bool HasConfiguredPrintSettings { get; set; }

    /// <summary>Records were edited in the app since they were last read from their source.</summary>
    internal bool RecordsEditedSinceLoad { get; set; }

    /// <summary>Zoom and scroll position when the tab was last shown.</summary>
    internal DesignerCanvas.ViewState? View { get; set; }

    public bool IsActive
    {
        get => _isActive;
        internal set => SetField(ref _isActive, value);
    }

    public string Title => Session.Document.FilePath is { } path
        ? Path.GetFileNameWithoutExtension(path)
        : string.IsNullOrWhiteSpace(Session.Project.Metadata.Title) ? "Untitled" : Session.Project.Metadata.Title;

    public bool HasUnsavedChanges => Session.Document.HasUnsavedChanges;

    public string ToolTip => Session.Document.FilePath ?? $"{Title} — not saved yet";

    /// <summary>
    /// A blank project nobody has touched. Opening or creating a project replaces it
    /// instead of leaving an empty "Untitled" tab behind.
    /// </summary>
    internal bool IsPristine => Session.Document.FilePath is null && !Session.Document.HasUnsavedChanges && !Session.History.CanUndo;

    internal void RefreshHeader()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ToolTip));
    }
}
