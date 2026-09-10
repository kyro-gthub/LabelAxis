using System.Text;
using LabelMaker.Core.Data;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;

namespace LabelMaker.Core.Search;

public enum SearchResultKind
{
    Page = 0,
    MasterObject = 1,
    DataRecord = 2,
    Template = 3,
    Sequence = 4,
}

/// <summary>A hit that the UI can navigate to directly (spec §24).</summary>
public sealed class SearchResult
{
    public required SearchResultKind Kind { get; init; }
    public required string Title { get; init; }
    public string Snippet { get; init; } = string.Empty;
    public double Score { get; init; }

    public string? PageId { get; init; }
    public string? ObjectId { get; init; }
    public int? RecordIndex { get; init; }
    public string? TemplateId { get; init; }
    public string? SequenceId { get; init; }
}

internal sealed class SearchEntry
{
    public SearchResultKind Kind;
    public string Title = string.Empty;
    public string Haystack = string.Empty;       // lower-cased, space-joined
    public string RawSnippetSource = string.Empty;
    public string? PageId;
    public string? ObjectId;
    public int? RecordIndex;
    public string? TemplateId;
    public string? SequenceId;
}

/// <summary>
/// A rebuildable in-memory index over a project: pages, master-label objects,
/// data records, template + sequence names. Search stays fast for large projects
/// because labels are records, not documents (spec §5, §27).
/// </summary>
public sealed class ProjectSearchIndex
{
    private readonly List<SearchEntry> _entries = new();

    public int EntryCount => _entries.Count;

    public void Rebuild(ProjectModel project, DataSet? activeData = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _entries.Clear();

        IndexDocument(project.MasterLabel, pageId: null, "Master label");

        foreach (LabelPage page in project.Pages)
        {
            var parts = new List<string> { page.Name, page.Id, page.Description ?? string.Empty };
            parts.AddRange(page.Tags);
            parts.AddRange(CollectDocumentText(page.Document));

            _entries.Add(new SearchEntry
            {
                Kind = SearchResultKind.Page,
                Title = page.Name,
                PageId = page.Id,
                RawSnippetSource = string.Join(" · ", parts.Where(p => p.Length > 0)),
                Haystack = Normalize(parts),
            });

            IndexDocument(page.Document, page.Id, page.Name);
        }

        if (project.Template is { } template)
        {
            _entries.Add(new SearchEntry
            {
                Kind = SearchResultKind.Template,
                Title = $"{template.Brand} {template.ProductCode} — {template.Name}",
                TemplateId = template.Id,
                RawSnippetSource = string.Join(" ", new[] { template.Id, template.Name, template.ProductCode, template.Brand }.Concat(template.Categories)),
                Haystack = Normalize(new[] { template.Id, template.Name, template.ProductCode, template.Brand }.Concat(template.Categories)),
            });
        }

        foreach (var sequence in project.Sequences)
        {
            _entries.Add(new SearchEntry
            {
                Kind = SearchResultKind.Sequence,
                Title = sequence.Name,
                SequenceId = sequence.Id,
                RawSnippetSource = $"{sequence.Name} {sequence.Prefix}#{sequence.Suffix}",
                Haystack = Normalize(new[] { sequence.Name, sequence.Prefix, sequence.Suffix, sequence.Pattern ?? string.Empty }),
            });
        }

        if (activeData is { RowCount: > 0 })
        {
            IndexData(activeData);
        }
    }

    private void IndexDocument(LabelDocument document, string? pageId, string documentTitle)
    {
        foreach (LabelObject obj in document.Objects)
        {
            var strings = obj.GetSearchableStrings().Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (strings.Count == 0 && string.IsNullOrEmpty(obj.Name))
            {
                continue;
            }

            string kindLabel = obj switch
            {
                BarcodeObject => "Barcode",
                TextObject => "Text",
                ImageObject => "Image",
                _ => obj.Kind.ToString(),
            };

            _entries.Add(new SearchEntry
            {
                Kind = SearchResultKind.MasterObject,
                Title = $"{kindLabel}: {(strings.FirstOrDefault() ?? obj.Name)}".Trim(),
                PageId = pageId,
                ObjectId = obj.Id,
                RawSnippetSource = $"{documentTitle} → {string.Join(" ", strings)}",
                Haystack = Normalize(strings.Append(obj.Name).Append(kindLabel)),
            });
        }
    }

    private void IndexData(DataSet data)
    {
        for (int r = 0; r < data.RowCount; r++)
        {
            DataRecord row = data.Rows[r];
            var sb = new StringBuilder();
            foreach (string key in data.ColumnIndex.Keys)
            {
                sb.Append(row[key]).Append(' ');
            }

            _entries.Add(new SearchEntry
            {
                Kind = SearchResultKind.DataRecord,
                Title = $"Record {r + 1}",
                RecordIndex = r,
                RawSnippetSource = sb.ToString().Trim(),
                Haystack = sb.ToString().ToLowerInvariant(),
            });
        }
    }

    /// <summary>Ranked search across every indexed entry.</summary>
    public IReadOnlyList<SearchResult> Search(string query, int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<SearchResult>();
        }

        string q = query.Trim().ToLowerInvariant();
        string[] terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var results = new List<SearchResult>();
        foreach (SearchEntry entry in _entries)
        {
            double score = ScoreEntry(entry, q, terms);
            if (score <= 0)
            {
                continue;
            }

            results.Add(new SearchResult
            {
                Kind = entry.Kind,
                Title = entry.Title,
                Snippet = MakeSnippet(entry.RawSnippetSource, terms),
                Score = score,
                PageId = entry.PageId,
                ObjectId = entry.ObjectId,
                RecordIndex = entry.RecordIndex,
                TemplateId = entry.TemplateId,
                SequenceId = entry.SequenceId,
            });
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Kind)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Fast path for "find the record containing this exact/near value" — the
    /// "barcode ABC001459 among 5000 labels" case (spec §5). O(records × columns).
    /// </summary>
    public IReadOnlyList<int> FindRecords(DataSet data, string value, bool exact = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrEmpty(value))
        {
            return Array.Empty<int>();
        }

        var hits = new List<int>();
        for (int r = 0; r < data.RowCount; r++)
        {
            DataRecord row = data.Rows[r];
            foreach (string key in data.ColumnIndex.Keys)
            {
                string cell = row[key];
                bool match = exact
                    ? string.Equals(cell, value, StringComparison.OrdinalIgnoreCase)
                    : cell.Contains(value, StringComparison.OrdinalIgnoreCase);
                if (match)
                {
                    hits.Add(r);
                    break;
                }
            }
        }

        return hits;
    }

    private static double ScoreEntry(SearchEntry entry, string wholeQuery, string[] terms)
    {
        string hay = entry.Haystack;
        double score = 0;

        if (hay.Contains(wholeQuery, StringComparison.Ordinal))
        {
            score += 10;
            if (entry.Title.ToLowerInvariant().Contains(wholeQuery, StringComparison.Ordinal))
            {
                score += 8;
            }
        }

        int matchedTerms = 0;
        foreach (string term in terms)
        {
            if (hay.Contains(term, StringComparison.Ordinal))
            {
                matchedTerms++;
                score += 2;
                if (WordBoundaryMatch(hay, term))
                {
                    score += 1.5;
                }
            }
        }

        if (matchedTerms == 0)
        {
            return 0;
        }

        if (matchedTerms < terms.Length)
        {
            score *= matchedTerms / (double)terms.Length;
        }

        // Prefer concrete navigation targets slightly.
        if (entry.Kind is SearchResultKind.Page or SearchResultKind.MasterObject)
        {
            score += 0.5;
        }

        return score;
    }

    private static bool WordBoundaryMatch(string haystack, string term)
    {
        int idx = haystack.IndexOf(term, StringComparison.Ordinal);
        while (idx >= 0)
        {
            bool leftOk = idx == 0 || !char.IsLetterOrDigit(haystack[idx - 1]);
            int end = idx + term.Length;
            bool rightOk = end == haystack.Length || !char.IsLetterOrDigit(haystack[end]);
            if (leftOk && rightOk)
            {
                return true;
            }

            idx = haystack.IndexOf(term, idx + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static string MakeSnippet(string source, string[] terms)
    {
        if (source.Length <= 120)
        {
            return source;
        }

        string lower = source.ToLowerInvariant();
        int at = -1;
        foreach (string term in terms)
        {
            at = lower.IndexOf(term, StringComparison.Ordinal);
            if (at >= 0)
            {
                break;
            }
        }

        if (at < 0)
        {
            return source[..120] + "…";
        }

        int start = Math.Max(0, at - 40);
        int len = Math.Min(source.Length - start, 120);
        string prefix = start > 0 ? "…" : string.Empty;
        string suffix = start + len < source.Length ? "…" : string.Empty;
        return prefix + source.Substring(start, len) + suffix;
    }

    private static IEnumerable<string> CollectDocumentText(LabelDocument document) =>
        document.Objects.SelectMany(o => o.GetSearchableStrings());

    private static string Normalize(IEnumerable<string> parts) =>
        string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p))).ToLowerInvariant();
}
