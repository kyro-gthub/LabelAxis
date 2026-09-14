using System.IO;
using System.Runtime.InteropServices;
using LabelMaker.Core.Model;
using LabelMaker.Data.Publisher;
using HAlign = LabelMaker.Core.Model.Text.HorizontalAlignment;
using VAlign = LabelMaker.Core.Model.Text.VerticalAlignment;

namespace LabelMaker.App.Services;

/// <summary>
/// Reads a .pub file through Microsoft Publisher itself (its automation interface), so pages,
/// positions, sizes, rotation, text formatting, fills, lines and pictures come across as
/// Publisher lays them out. Only works where Publisher is installed; the file is opened
/// read-only in a hidden Publisher window and closed without saving.
/// <para>
/// Shapes LabelAxis has no equivalent for (WordArt, freeforms, charts, callouts, OLE
/// objects) are brought in as a picture of themselves, so the page still looks the same.
/// </para>
/// </summary>
public static class PublisherAutomation
{
    private const int MsoTrue = -1;

    // Publisher / Office shape type numbers (PbShapeType, same values as MsoShapeType).
    private const int TypeAutoShape = 1;
    private const int TypeGroup = 6;
    private const int TypeLine = 9;
    private const int TypeLinkedPicture = 11;
    private const int TypePicture = 13;
    private const int TypeTextFrame = 17;
    private const int TypeTable = 19;

    // MsoAutoShapeType values LabelAxis draws natively.
    private const int ShapeRectangle = 1;
    private const int ShapeRoundedRectangle = 5;
    private const int ShapeOval = 9;

    /// <summary>True when Microsoft Publisher is registered on this PC.</summary>
    public static bool IsInstalled => Type.GetTypeFromProgID("Publisher.Application") is not null;

    /// <summary>
    /// Opens <paramref name="path"/> in Publisher on a background STA thread and reads every
    /// page. Progress reports pages read; cancelling stops after the current page.
    /// </summary>
    public static Task<PubLayout> ReadAsync(string path, IProgress<int>? progress, CancellationToken cancellation)
    {
        var done = new TaskCompletionSource<PubLayout>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.TrySetResult(Read(path, progress, cancellation));
            }
            catch (OperationCanceledException ex)
            {
                done.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Publisher import",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static PubLayout Read(string path, IProgress<int>? progress, CancellationToken cancellation)
    {
        Type type = Type.GetTypeFromProgID("Publisher.Application")
                    ?? throw new InvalidOperationException("Microsoft Publisher is not installed on this PC.");

        dynamic? app = null;
        dynamic? doc = null;
        string pictures = Path.Combine(Path.GetTempPath(), "LabelAxis-pub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pictures);
        var notes = new List<string>();

        try
        {
            app = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Publisher could not be started.");
            doc = app.Open(Path.GetFullPath(path), true, false);
            TryDo(() => app.ActiveWindow.Visible = false);

            double pageWidth = (double)doc.PageSetup.PageWidth;
            double pageHeight = (double)doc.PageSetup.PageHeight;
            int count = (int)doc.Pages.Count;
            var pages = new List<PubPage>(count);
            var reader = new ShapeReader(pictures, notes);

            for (int i = 1; i <= count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                dynamic page = doc.Pages.Item(i);
                var shapes = new List<PubShape>();

                // Master page items (headers, backgrounds) sit behind the page's own shapes.
                if (TryGet(() => page.Master) is { } master)
                {
                    reader.ReadAll(master.Shapes, shapes, $"page {i} master");
                }

                reader.ReadAll(page.Shapes, shapes, $"page {i}");
                string name = TryGet(() => (string)page.Name) is { Length: > 0 } n ? n : $"Page {i}";
                pages.Add(new PubPage(name, shapes));
                progress?.Report(i);
            }

            string title = Path.GetFileNameWithoutExtension(path);
            return new PubLayout(title, pageWidth, pageHeight, pages) { Notes = notes };
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException("Microsoft Publisher could not read the file: " + ex.Message, ex);
        }
        finally
        {
            TryDo(() => doc?.Close());
            TryDo(() => app?.Quit());
            if (doc is not null)
            {
                Release(doc);
            }

            if (app is not null)
            {
                Release(app);
            }

            try
            {
                Directory.Delete(pictures, recursive: true);
            }
            catch (IOException)
            {
                // Temp files; Windows cleans them up eventually.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class ShapeReader
    {
        private readonly string _pictures;
        private readonly List<string> _notes;
        private int _groups;
        private int _pictureNumber;

        public ShapeReader(string pictures, List<string> notes)
        {
            _pictures = pictures;
            _notes = notes;
        }

        public void ReadAll(dynamic shapes, List<PubShape> into, string where)
        {
            int count = (int)shapes.Count;
            for (int j = 1; j <= count; j++)
            {
                dynamic shape = shapes.Item(j);
                try
                {
                    Read(shape, into, null);
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    string name = TryGet(() => (string)shape.Name) ?? $"shape {j}";
                    _notes.Add($"Skipped \"{name}\" on {where}: {ex.Message}");
                }
            }
        }

        private void Read(dynamic shape, List<PubShape> into, string? group)
        {
            int type = (int)shape.Type;
            if (type == TypeGroup)
            {
                string key = $"group{++_groups}";
                int count = (int)shape.GroupItems.Count;
                for (int k = 1; k <= count; k++)
                {
                    Read(shape.GroupItems.Item(k), into, group ?? key);
                }

                return;
            }

            var basis = new PubShape
            {
                Name = TryGet(() => (string)shape.Name) ?? string.Empty,
                LeftPt = (double)shape.Left,
                TopPt = (double)shape.Top,
                WidthPt = (double)shape.Width,
                HeightPt = (double)shape.Height,
                RotationDegrees = TryGet(() => (double)shape.Rotation),
                Fill = FillOf(shape),
                Line = LineOf(shape),
                LineWeightPt = TryGet(() => (double)shape.Line.Weight) is var w and > 0 ? w : 0.75,
                GroupKey = group,
            };

            if (type is TypePicture or TypeLinkedPicture)
            {
                into.Add(Picture(shape, basis));
                return;
            }

            if (type == TypeLine)
            {
                into.Add(basis with { Kind = PubShapeKind.Line, Fill = null, Line = basis.Line ?? ColorRgba.Black });
                return;
            }

            if (type == TypeTable)
            {
                // Tables keep their look as a picture; their text is still searchable in the text import.
                into.Add(Picture(shape, basis));
                return;
            }

            bool hasText = TryGet(() => (int)shape.HasTextFrame) == MsoTrue;
            IReadOnlyList<PubParagraph> paragraphs = hasText ? ParagraphsOf(shape) : Array.Empty<PubParagraph>();

            if (type == TypeTextFrame)
            {
                into.Add(basis with
                {
                    Kind = PubShapeKind.Text,
                    Paragraphs = paragraphs,
                    VerticalAlignment = VerticalOf(shape),
                    InsetsPt = InsetsOf(shape),
                });
                return;
            }

            if (type == TypeAutoShape)
            {
                int auto = TryGet(() => (int)shape.AutoShapeType);
                PubShapeKind? kind = auto switch
                {
                    ShapeRectangle => PubShapeKind.Rectangle,
                    ShapeRoundedRectangle => PubShapeKind.RoundRectangle,
                    ShapeOval => PubShapeKind.Ellipse,
                    _ => null,
                };

                if (kind is { } native)
                {
                    into.Add(basis with
                    {
                        Kind = native,
                        Paragraphs = paragraphs.Any(p => p.Runs.Any(r => r.Text.Trim().Length > 0)) ? paragraphs : Array.Empty<PubParagraph>(),
                        VerticalAlignment = VerticalOf(shape),
                        InsetsPt = InsetsOf(shape),
                    });
                    return;
                }
            }

            // Everything else looks right as a picture of itself.
            into.Add(Picture(shape, basis));
        }

        private PubShape Picture(dynamic shape, PubShape basis)
        {
            string file = Path.Combine(_pictures, $"shape{++_pictureNumber}.png");
            try
            {
                shape.SaveAsPicture(file);
                if (File.Exists(file))
                {
                    return basis with { Kind = PubShapeKind.Picture, ImageBytes = File.ReadAllBytes(file), ImageExtension = ".png", Fill = null, Line = null };
                }
            }
            catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or IOException)
            {
                _notes.Add($"\"{basis.Name}\" could not be saved as a picture ({ex.Message}); an outline marks its place.");
            }

            return basis with { Kind = PubShapeKind.Rectangle, Fill = null, Line = new ColorRgba(0x99, 0x99, 0x99) };
        }

        private static IReadOnlyList<PubParagraph> ParagraphsOf(dynamic shape)
        {
            var result = new List<PubParagraph>();
            dynamic range = shape.TextFrame.TextRange;
            string all = TryGet(() => (string)range.Text) ?? string.Empty;
            string[] texts = all.Replace("\r\n", "\r").TrimEnd('\r').Split('\r');

            int start = 1;
            foreach (string paragraphText in texts)
            {
                string text = paragraphText.Replace('\v', '\n');
                int length = paragraphText.Length;
                dynamic? part = length > 0 ? TryGet(() => range.Characters(start, length)) : null;
                PubRun run = RunOf(part ?? range, text);
                HAlign align = TryGet(() => (int)(part ?? range).ParagraphFormat.Alignment) switch
                {
                    1 => HAlign.Center,
                    2 => HAlign.Right,
                    3 or 4 => HAlign.Justify,
                    _ => HAlign.Left,
                };
                result.Add(new PubParagraph(new[] { run }, align));
                start += length + 1;
            }

            return result;
        }

        private static PubRun RunOf(dynamic range, string text)
        {
            dynamic font = range.Font;
            return new PubRun(
                text,
                TryGet(() => (string)font.Name) ?? string.Empty,
                TryGet(() => (double)font.Size),
                TryGet(() => (int)font.Bold) == MsoTrue,
                TryGet(() => (int)font.Italic) == MsoTrue,
                TryGet(() => (int)font.Underline) is var u && u != 0 && u != -2,
                TryGet(() => (int)font.Color.RGB) is int rgb ? FromBgr(rgb) : ColorRgba.Black);
        }

        private static VAlign VerticalOf(dynamic shape) => TryGet(() => (int)shape.TextFrame.VerticalTextAlignment) switch
        {
            2 => VAlign.Middle,
            3 => VAlign.Bottom,
            _ => VAlign.Top,
        };

        private static (double, double, double, double) InsetsOf(dynamic shape) =>
            (TryGet(() => (double)shape.TextFrame.MarginLeft, 7.2), TryGet(() => (double)shape.TextFrame.MarginTop, 3.6),
             TryGet(() => (double)shape.TextFrame.MarginRight, 7.2), TryGet(() => (double)shape.TextFrame.MarginBottom, 3.6));

        private static ColorRgba? FillOf(dynamic shape)
        {
            if (TryGet(() => (int)shape.Fill.Visible) != MsoTrue)
            {
                return null;
            }

            ColorRgba colour = FromBgr(TryGet(() => (int)shape.Fill.ForeColor.RGB));
            double transparency = TryGet(() => (double)shape.Fill.Transparency);
            return colour with { A = (byte)Math.Round(255 * (1 - Math.Clamp(transparency, 0, 1))) };
        }

        private static ColorRgba? LineOf(dynamic shape) =>
            TryGet(() => (int)shape.Line.Visible) == MsoTrue ? FromBgr(TryGet(() => (int)shape.Line.ForeColor.RGB)) : null;
    }

    /// <summary>Office colours are 0x00BBGGRR.</summary>
    private static ColorRgba FromBgr(int bgr) => new((byte)(bgr & 0xFF), (byte)((bgr >> 8) & 0xFF), (byte)((bgr >> 16) & 0xFF));

    private static T? TryGet<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException or NotImplementedException or System.Reflection.TargetInvocationException)
        {
            return default;
        }
    }

    private static T TryGet<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException or NotImplementedException or System.Reflection.TargetInvocationException)
        {
            return fallback;
        }
    }

    private static void TryDo(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
        {
            // Best effort while closing.
        }
    }

    private static void Release(object com)
    {
        try
        {
            if (Marshal.IsComObject(com))
            {
                Marshal.FinalReleaseComObject(com);
            }
        }
        catch (ArgumentException)
        {
        }
    }

}
