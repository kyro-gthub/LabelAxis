using System.Text.Json;
using LabelMaker.Core.Model;
using LabelMaker.Core.Serialization;

namespace LabelMaker.App.Services;

/// <summary>
/// The print windows write their choices (printer, paper, margins, copies, thermal settings…)
/// straight into the project's print settings as they change. These notice, when such a window
/// closes, whether anything that is saved with the project changed, and then mark the project
/// changed, so it is saved, auto-saved and asked about like any other edit. Before, choices made
/// in the Print window were lost when the project was opened again.
/// </summary>
public static class PrintSettingsWatch
{
    /// <summary>The settings as the project file saves them (choices for one job only are left out).</summary>
    public static string Snapshot(PrintSettings settings) => JsonSerializer.Serialize(settings, ProjectJson.Default);

    /// <summary>Marks the project changed when its saved print settings differ from <paramref name="before"/>.</summary>
    public static bool MarkIfChanged(DocumentSession session, string? before)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (before is null || string.Equals(before, Snapshot(session.Project.PrintSettings), StringComparison.Ordinal))
        {
            return false;
        }

        session.MarkDirty();
        return true;
    }
}
