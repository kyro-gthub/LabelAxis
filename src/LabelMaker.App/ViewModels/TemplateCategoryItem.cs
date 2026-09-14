using LabelMaker.Core.Formtec;

namespace LabelMaker.App.ViewModels;

/// <summary>One row in the left panel's template browser: a named group of sheets plus its count.</summary>
public sealed record TemplateCategoryItem(
    string Title,
    string Subtitle,
    string IconKey,
    IReadOnlyList<LabelTemplate> Templates)
{
    public int Count => Templates.Count;

    // Lists use this for screen readers and type-to-search; a record would give its field dump.
    public override string ToString() => Title;
}

/// <summary>A single sheet inside a category, as shown in the template list.</summary>
public sealed record TemplateListItem(LabelTemplate Template)
{
    public string Code => string.IsNullOrWhiteSpace(Template.ProductCode) ? Template.Name : Template.ProductCode;

    public string SizeLine => $"{Template.Grid.LabelWidthMm:0.##} × {Template.Grid.LabelHeightMm:0.##} mm";

    public string CountLine =>
        $"{Template.Grid.Columns * Template.Grid.Rows} labels ({Template.Grid.Columns} × {Template.Grid.Rows})";

    public override string ToString() => $"{Template.Brand} {Code}".Trim();
}
