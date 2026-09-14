namespace LabelMaker.App.ViewModels;

/// <summary>A row in the left-hand page navigator. <see cref="PageId"/> is null for the master label.</summary>
public sealed record PageListItem(string? PageId, string Name, int Number, bool IsMaster)
{
    // The master label shows its own name too, or renaming it looked as if nothing happened.
    public string Display => IsMaster ? $"★  {Name}" : $"{Number}.  {Name}";

    public override string ToString() => Display;
}
