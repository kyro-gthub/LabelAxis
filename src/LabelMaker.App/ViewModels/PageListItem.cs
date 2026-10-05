namespace LabelMaker.App.ViewModels;

/// <summary>A row in the left-hand page navigator. <see cref="PageId"/> is null for the master label.</summary>
public sealed record PageListItem(string? PageId, string Name, int Number, bool IsMaster)
{
    // Numbered from 1 in order, the first page too; each row shows the page's own name as well,
    // or renaming it looked as if nothing happened.
    public string Display => $"{Number}.  {Name}";

    public override string ToString() => Display;
}
