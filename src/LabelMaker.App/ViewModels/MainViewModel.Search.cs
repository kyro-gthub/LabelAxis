using System.Collections.ObjectModel;
using System.IO;
using LabelMaker.App.Controls;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Printing;
using LabelMaker.App.Rendering;
using LabelMaker.App.Services;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Bulk;
using LabelMaker.Core.Data;
using LabelMaker.Core.Data.Import;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Preflight;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using LabelMaker.Export;
using LabelMaker.Export.Pdf;
using LabelMaker.Export.Svg;

namespace LabelMaker.App.ViewModels;

/// <summary>Search and the object and page lists.</summary>
public sealed partial class MainViewModel
{
    // ---------- search / lists ----------

    private void RefreshSearch()
    {
        _session.RebuildSearchIndex();
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return;
        }

        foreach (var result in _session.SearchIndex.Search(_searchText, 60))
        {
            SearchResults.Add(new SearchListItem(result));
        }
    }

    private void NavigateToSearchResult(SearchListItem item)
    {
        var r = item.Result;
        if (r.RecordIndex is { } record)
        {
            _session.RecordIndex = record;
        }

        if (r.ObjectId is { } objectId)
        {
            LabelObject? obj = _session.ActiveLabel.FindObject(objectId);
            if (obj is not null)
            {
                _session.SetSelection(new[] { obj });
            }
        }

        StatusText = $"Jumped to {r.Title}";
    }

    private void RefreshRecent()
    {
        _env.Settings.PruneMissingRecents();
        RecentProjects.Clear();
        foreach (var r in _env.Settings.RecentProjects)
        {
            RecentProjects.Add(new RecentEntry(r.Path, r.Title));
        }
    }

    private void RefreshCanvas() => _canvas?.InvalidateVisual();

    /// <summary>Draws the label again (after a font was removed in Settings).</summary>
    public void RedrawCanvas() => RefreshCanvas();

    private void OnDocumentChanged()
    {
        _activeTab?.RefreshHeader();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SelectionInfo));
        OnPropertyChanged(nameof(DocumentTabTitle));
        OnPropertyChanged(nameof(SaveStateText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(SheetSummary));
        OnPropertyChanged(nameof(TemplateSummary));
        OnPropertyChanged(nameof(SharedGuides));
        OnPropertyChanged(nameof(LockGuides));
        RebuildObjectList();
        RefreshCanvas();
        RaiseAllCanExecute();
    }

    private void OnSelectionChanged()
    {
        SyncObjectListSelection();
        OnPropertyChanged(nameof(SelectionInfo));
        _properties.RefreshAll();
        RefreshCanvas();
        RaiseAllCanExecute();
    }

    private void RaiseAllCanExecute()
    {
        // Every command, not a hand-kept list: the list went stale as commands were
        // added, leaving menu items greyed out (or live) after the selection changed.
        foreach (System.Reflection.PropertyInfo property in CommandProperties)
        {
            (property.GetValue(this) as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private static readonly System.Reflection.PropertyInfo[] CommandProperties =
        typeof(MainViewModel).GetProperties().Where(p => p.PropertyType == typeof(RelayCommand)).ToArray();

    private static string SafeName(string title)
    {
        Span<char> buffer = stackalloc char[title.Length == 0 ? 1 : title.Length];
        int n = 0;
        foreach (char c in title)
        {
            buffer[n++] = Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c;
        }

        return n == 0 ? "label" : new string(buffer[..n]);
    }
}
