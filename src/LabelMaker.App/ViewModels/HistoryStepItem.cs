namespace LabelMaker.App.ViewModels;

/// <summary>
/// A row in the History tab. Choosing it undoes or redoes until <see cref="DoneCount"/> steps
/// are done; the first row (0) is the project as it was opened.
/// </summary>
public sealed record HistoryStepItem(int DoneCount, string Name, bool IsCurrent, bool IsUndone)
{
    public string Display => DoneCount == 0 ? Name : $"{DoneCount}.  {Name}";

    public override string ToString() => Display;
}
