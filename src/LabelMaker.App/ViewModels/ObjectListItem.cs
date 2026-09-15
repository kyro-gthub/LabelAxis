using LabelMaker.App.Mvvm;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Search;

namespace LabelMaker.App.ViewModels;

/// <summary>
/// A row in "Objects on page" and the Layers list: one object, or a group heading whose
/// members follow it indented. <see cref="IsSelected"/> mirrors the canvas selection so
/// the list can select several rows at once.
/// </summary>
public sealed class ObjectListItem : ObservableObject
{
    private bool _isSelected;

    private ObjectListItem(LabelObject? obj, string? groupId, IReadOnlyList<LabelObject> members, bool isMember)
    {
        Object = obj;
        GroupId = groupId;
        Members = members;
        IsGroupMember = isMember;
    }

    public static ObjectListItem ForObject(LabelObject obj, bool inGroup) => new(obj, obj.GroupId, new[] { obj }, inGroup);

    public static ObjectListItem ForGroup(string groupId, IReadOnlyList<LabelObject> members) => new(null, groupId, members, isMember: false);

    /// <summary>The object on this row; null for a group heading.</summary>
    public LabelObject? Object { get; }

    public string? GroupId { get; }

    /// <summary>The objects this row stands for: every member for a group heading, otherwise just the one.</summary>
    public IReadOnlyList<LabelObject> Members { get; }

    public bool IsGroup => Object is null;

    public bool IsGroupMember { get; }

    /// <summary>Identifies the row across rebuilds, so the list can be refreshed in place.</summary>
    public string Key => IsGroup ? "g:" + GroupId : "o:" + Object!.Id;

    public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }

    public bool Locked => Members.Count > 0 && Members.All(m => m.Locked);

    /// <summary>The row's text: the name the user gave the object, or its kind and content when it has none.</summary>
    public string Display
    {
        get
        {
            string hideMark = Members.Count > 0 && Members.All(m => !m.Visible) ? " (hidden)" : string.Empty;
            if (IsGroup)
            {
                return $"Group — {Members.Count} objects{hideMark}";
            }

            LabelObject o = Object!;
            if (!string.IsNullOrWhiteSpace(o.Name))
            {
                return o.Name + hideMark;
            }

            string detail = o switch
            {
                TextObject t => Trim(t.Content.PlainText),
                BarcodeObject b => $"{b.Spec.Symbology} {Trim(b.Spec.Data)}",
                ImageObject i => i.SourceFileName ?? "image",
                ShapeObject s => s.Shape.ToString(),
                _ => string.Empty,
            };
            return (detail.Length == 0 ? o.Kind.ToString() : $"{o.Kind} — {detail}") + hideMark;
        }
    }

    /// <summary>Only single objects have names; a group heading cannot be renamed.</summary>
    public bool CanRename => !IsGroup;

    private bool _isEditing;

    /// <summary>True while the row shows a text box for renaming.</summary>
    public bool IsEditing { get => _isEditing; set => SetField(ref _isEditing, value); }

    /// <summary>The text the rename box starts with.</summary>
    public string EditName => Object?.Name is { Length: > 0 } name ? name : Display;

    /// <summary>Re-reads the object's name, lock and visibility after an edit.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(EditName));
        OnPropertyChanged(nameof(Locked));
    }

    public override string ToString() => Display;

    private static string Trim(string s) => s.Length <= 30 ? s : s[..30] + "…";
}

public sealed record SearchListItem(SearchResult Result)
{
    public string Display => $"[{Result.Kind}] {Result.Title}" +
                             (string.IsNullOrEmpty(Result.Snippet) ? string.Empty : $"  ·  {Result.Snippet}");
}
