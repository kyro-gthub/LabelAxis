using System.Collections.ObjectModel;
using LabelMaker.App.Mvvm;

namespace LabelMaker.App.ViewModels;

/// <summary>The History tab: every step that can be undone or redone, to go straight to one.</summary>
public sealed partial class MainViewModel
{
    /// <summary>The right panel's History tab (see <see cref="SelectedRightTab"/>).</summary>
    public const int HistoryTab = 1;

    public RelayCommand ShowHistoryCommand { get; private set; } = null!;
    public RelayCommand GoToHistoryStepCommand { get; private set; } = null!;

    /// <summary>The start, then the done steps oldest first, then the undone steps that can be redone.</summary>
    public ObservableCollection<HistoryStepItem> HistorySteps { get; } = new();

    private void BuildHistoryCommands()
    {
        ShowHistoryCommand = new RelayCommand(() => SelectedRightTab = HistoryTab);
        GoToHistoryStepCommand = new RelayCommand(p =>
        {
            if (p is HistoryStepItem step)
            {
                GoToHistoryStep(step);
            }
        });
    }

    public void GoToHistoryStep(HistoryStepItem step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _session.GoToHistory(step.DoneCount);
    }

    private void RebuildHistory()
    {
        IReadOnlyList<string> done = _session.History.UndoLabels;
        IReadOnlyList<string> undone = _session.History.RedoLabels;
        var rows = new List<HistoryStepItem>(done.Count + undone.Count + 1)
        {
            new(0, Localization.Localizer.T("Project as opened"), done.Count == 0, false),
        };
        for (int i = 0; i < done.Count; i++)
        {
            rows.Add(new HistoryStepItem(i + 1, Localization.Localizer.T(done[i]), i == done.Count - 1, false));
        }

        for (int i = 0; i < undone.Count; i++)
        {
            rows.Add(new HistoryStepItem(done.Count + i + 1, Localization.Localizer.T(undone[i]), false, true));
        }

        if (rows.SequenceEqual(HistorySteps))
        {
            return;
        }

        HistorySteps.Clear();
        foreach (HistoryStepItem row in rows)
        {
            HistorySteps.Add(row);
        }
    }
}
