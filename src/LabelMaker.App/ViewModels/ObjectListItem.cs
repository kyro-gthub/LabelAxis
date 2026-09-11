using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Search;

namespace LabelMaker.App.ViewModels;

public sealed record ObjectListItem(LabelObject Object)
{
    public string Display
    {
        get
        {
            string name = string.IsNullOrWhiteSpace(Object.Name) ? Object.Kind.ToString() : Object.Name;
            string detail = Object switch
            {
                TextObject t => Trim(t.Content.PlainText),
                BarcodeObject b => $"{b.Spec.Symbology} {Trim(b.Spec.Data)}",
                ImageObject i => i.SourceFileName ?? "image",
                ShapeObject s => s.Shape.ToString(),
                _ => string.Empty,
            };
            string hideMark = Object.Visible ? string.Empty : " (hidden)";
            return $"{name} — {detail}{hideMark}";
        }
    }

    private static string Trim(string s) => s.Length <= 30 ? s : s[..30] + "…";
}

public sealed record SearchListItem(SearchResult Result)
{
    public string Display => $"[{Result.Kind}] {Result.Title}" +
                             (string.IsNullOrEmpty(Result.Snippet) ? string.Empty : $"  ·  {Result.Snippet}");
}
