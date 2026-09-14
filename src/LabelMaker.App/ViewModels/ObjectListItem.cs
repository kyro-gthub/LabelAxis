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

    public string Display
    {
        get
        {
            if (IsGroup)
            {
                return $"Group — {Members.Count} objects" + (Members.All(m => !m.Visible) ? " (hidden)" : string.Empty);
            }

            LabelObject o = Object!;
            string name = string.IsNullOrWhiteSpace(o.Name) ? o.Kind.ToString() : o.Name;
            string detail = o switch
            {
                TextObject t => Trim(t.Content.PlainText),
                BarcodeObject b => $"{b.Spec.Symbology} {Trim(b.Spec.Data)}",
                ImageObject i => i.SourceFileName ?? "image",
                ShapeObject s => s.Shape.ToString(),
                _ => string.Empty,
            };
            string hideMark = o.Visible ? string.Empty : " (hidden)";
            return $"{name} — {detail}{hideMark}";
        }
    }

    /// <summary>Re-reads the object's name, lock and visibility after an edit.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Display));
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
