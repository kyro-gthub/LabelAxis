namespace LabelMaker.App.ViewModels;

/// <summary>A row in the left-hand page navigator. <see cref="PageId"/> is null for the master label.</summary>
public sealed record PageListItem(string? PageId, string Name, int Number, bool IsMaster)
{
    public string Display => IsMaster ? "★  Master label" : $"{Number}.  {Name}";
}
