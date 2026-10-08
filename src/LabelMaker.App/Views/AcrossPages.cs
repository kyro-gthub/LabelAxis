using System.Windows.Controls;
using LabelMaker.App.Localization;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Views;

/// <summary>What the windows that work on the selection across pages (Apply page format, Remove from all pages) share.</summary>
internal static class AcrossPages
{
    /// <summary>"The selected object (“Price”)" or "The 3 selected objects".</summary>
    public static string SelectionCaption(IReadOnlyList<LabelObject> selected) =>
        selected.Count == 1
            ? Localizer.F("The selected object ({0})", Describe(selected[0]))
            : Localizer.F("The {0} selected objects", selected.Count);

    /// <summary>How objects are matched from the "Match each one … by" choices; name-then-order otherwise.</summary>
    public static LayoutMatch Match(RadioButton sameOrder, RadioButton sameName) =>
        sameOrder.IsChecked == true ? LayoutMatch.SameOrder
        : sameName.IsChecked == true ? LayoutMatch.SameName
        : LayoutMatch.NameThenOrder;

    /// <summary>A page's line: "2.  Page 2  ·  1 object" or "…  ·  3 objects".</summary>
    public static string ObjectCount(string caption, int count) =>
        count == 1 ? Localizer.F("{0}  ·  1 object", caption) : Localizer.F("{0}  ·  {1} objects", caption, count);

    /// <summary>The sentence shown when nothing on the ticked pages matches.</summary>
    public static string NothingMatches(LayoutMatch match) =>
        Localizer.T(match == LayoutMatch.SameName
            ? "Nothing on the ticked pages has the same name. Try matching by order."
            : "Nothing to match on the ticked pages.");

    private static string Describe(LabelObject o) =>
        string.IsNullOrWhiteSpace(o.Name) ? Localizer.T(o.Kind.ToString()) : $"“{o.Name.Trim()}”";
}
