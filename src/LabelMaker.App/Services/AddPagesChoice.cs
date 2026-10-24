namespace LabelMaker.App.Services;

/// <summary>
/// What the Add pages window asks for: <paramref name="Count"/> blank pages, or that many copies
/// of the page <paramref name="SourcePageId"/> (null is the master label), whose barcodes count up
/// page by page when <paramref name="CountUp"/>.
/// </summary>
public sealed record AddPagesChoice(int Count, bool Blank, string? SourcePageId, bool CountUp);
