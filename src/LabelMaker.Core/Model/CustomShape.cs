using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Units;

namespace LabelMaker.Core.Model;

/// <summary>
/// Turns dots placed on the label into a <see cref="ShapeKind.Custom"/> shape and back. The
/// shape keeps its dots as fractions of its box, so moving, resizing, rotating and printing
/// treat it like any other object.
/// </summary>
public static class CustomShape
{
    /// <summary>The thinnest a custom shape's box gets, so a straight line of dots still has a box.</summary>
    public const double MinSizeMm = 0.2;

    /// <summary>A new shape through <paramref name="dots"/> (document millimetres), boxed around them.</summary>
    public static ShapeObject Create(IReadOnlyList<PointMm> dots, bool closed = true)
    {
        ArgumentNullException.ThrowIfNull(dots);
        if (dots.Count < 2)
        {
            throw new ArgumentException("A shape needs at least two dots.", nameof(dots));
        }

        (RectMm box, List<PointMm> points) = Fit(dots);
        return new ShapeObject
        {
            Shape = ShapeKind.Custom,
            Bounds = box,
            CustomPoints = points,
            ClosedPath = closed && dots.Count >= 3,
            Fill = ColorRgba.Transparent,
            Stroke = ColorRgba.Black,
            StrokeWidthMm = 0.3,
        };
    }

    /// <summary>The box around <paramref name="dots"/> and each dot as a fraction of that box.</summary>
    public static (RectMm Box, List<PointMm> Points) Fit(IReadOnlyList<PointMm> dots)
    {
        ArgumentNullException.ThrowIfNull(dots);
        double minX = dots.Min(p => p.X), maxX = dots.Max(p => p.X);
        double minY = dots.Min(p => p.Y), maxY = dots.Max(p => p.Y);

        // A row of dots on one line still needs some height (and a column some width).
        if (maxX - minX < MinSizeMm)
        {
            double c = (minX + maxX) / 2.0;
            minX = c - (MinSizeMm / 2.0);
            maxX = c + (MinSizeMm / 2.0);
        }

        if (maxY - minY < MinSizeMm)
        {
            double c = (minY + maxY) / 2.0;
            minY = c - (MinSizeMm / 2.0);
            maxY = c + (MinSizeMm / 2.0);
        }

        var box = RectMm.FromLtrb(minX, minY, maxX, maxY);
        var points = dots.Select(p => new PointMm((p.X - minX) / box.Width, (p.Y - minY) / box.Height)).ToList();
        return (box, points);
    }

    /// <summary>The shape's dots inside <paramref name="local"/> (the box it is drawn in).</summary>
    public static List<PointMm> PointsIn(ShapeObject shape, RectMm local)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return shape.CustomPoints.Select(p => new PointMm(local.X + (p.X * local.Width), local.Y + (p.Y * local.Height))).ToList();
    }

    /// <summary>The shape's dots in document millimetres, with its rotation and skew applied.</summary>
    public static List<PointMm> WorldPoints(ShapeObject shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        double w = shape.Bounds.Width, h = shape.Bounds.Height;
        return shape.CustomPoints
            .Select(p => ObjectGeometry.ToWorld(shape, new PointMm((p.X - 0.5) * w, (p.Y - 0.5) * h)))
            .ToList();
    }

    /// <summary>
    /// Where the shape's box and dots end up after its dots were moved to
    /// <paramref name="world"/> (document millimetres). The box is fitted around the dots in
    /// the shape's own (rotated) frame, so a rotated shape stays rotated.
    /// </summary>
    public static (RectMm Bounds, List<PointMm> Points) Refit(ShapeObject shape, IReadOnlyList<PointMm> world)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(world);

        // Into the shape's frame (centre-origin, unrotated), fit a box there, then carry the
        // box's new centre back out through the same rotation.
        var local = world.Select(p => ObjectGeometry.ToLocal(shape, p)).ToList();
        (RectMm box, List<PointMm> points) = Fit(local);
        PointMm shift = ObjectGeometry.Linear(shape).ApplyVector(box.Center.X, box.Center.Y);
        PointMm centre = new(shape.Bounds.Center.X + shift.X, shape.Bounds.Center.Y + shift.Y);
        return (RectMm.FromCenter(centre, new SizeMm(box.Width, box.Height)), points);
    }

    /// <summary>How many straight pieces draw one curved stretch between two dots: smooth at any print size.</summary>
    public const int CurveSteps = 24;

    /// <summary>
    /// The outline the shape is drawn with inside <paramref name="local"/>: its dots joined by
    /// straight lines, except where a dot is curved — there the outline flows through it in a
    /// smooth curve. Curves are laid out as short straight pieces, so every printer, PDF and
    /// picture draws them the same way.
    /// </summary>
    public static List<PointMm> OutlineIn(ShapeObject shape, RectMm local)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return Outline(PointsIn(shape, local), shape.CurvedPoints, shape.ClosedPath && shape.CustomPoints.Count >= 3);
    }

    /// <summary>The outline through <paramref name="dots"/> (see <see cref="OutlineIn"/>).</summary>
    public static List<PointMm> Outline(IReadOnlyList<PointMm> dots, IReadOnlyList<bool>? curved, bool closed)
    {
        ArgumentNullException.ThrowIfNull(dots);
        var outline = new List<PointMm>();
        if (dots.Count == 0)
        {
            return outline;
        }

        outline.Add(dots[0]);
        int edges = closed ? dots.Count : dots.Count - 1;
        for (int i = 0; i < edges; i++)
        {
            List<PointMm> edge = Edge(dots, curved, closed, i);
            // The edge starts at the dot already in the outline; a closed outline ends where it began.
            outline.AddRange(edge.Skip(1).Take(closed && i == edges - 1 ? edge.Count - 2 : edge.Count - 1));
        }

        return outline;
    }

    /// <summary>
    /// The line from dot <paramref name="index"/> to the next one, as points from one dot to the
    /// other: just the two dots when neither is curved, otherwise a smooth curve. A curved dot
    /// gets a tangent along the line between its two neighbours (a Catmull-Rom curve).
    /// </summary>
    public static List<PointMm> Edge(IReadOnlyList<PointMm> dots, IReadOnlyList<bool>? curved, bool closed, int index)
    {
        ArgumentNullException.ThrowIfNull(dots);
        int n = dots.Count;
        PointMm At(int i) => closed ? dots[((i % n) + n) % n] : dots[Math.Clamp(i, 0, n - 1)];
        bool Curved(int i)
        {
            int k = closed ? ((i % n) + n) % n : i;
            return curved is not null && k >= 0 && k < curved.Count && curved[k];
        }

        PointMm a = At(index), b = At(index + 1);
        if (!Curved(index) && !Curved(index + 1))
        {
            return new List<PointMm> { a, b };
        }

        PointMm before = At(index - 1), after = At(index + 2);
        PointMm c1 = Curved(index) ? new PointMm(a.X + ((b.X - before.X) / 6), a.Y + ((b.Y - before.Y) / 6)) : a;
        PointMm c2 = Curved(index + 1) ? new PointMm(b.X - ((after.X - a.X) / 6), b.Y - ((after.Y - a.Y) / 6)) : b;

        var points = new List<PointMm>(CurveSteps + 1);
        for (int s = 0; s <= CurveSteps; s++)
        {
            double t = s / (double)CurveSteps, u = 1 - t;
            double w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
            points.Add(new PointMm(
                (w0 * a.X) + (w1 * c1.X) + (w2 * c2.X) + (w3 * b.X),
                (w0 * a.Y) + (w1 * c1.Y) + (w2 * c2.Y) + (w3 * b.Y)));
        }

        return points;
    }

    /// <summary>The curved-dot flags for <paramref name="count"/> dots: as given, missing ones sharp.</summary>
    public static List<bool> CurvedFlags(IReadOnlyList<bool>? curved, int count) =>
        Enumerable.Range(0, count).Select(i => curved is not null && i < curved.Count && curved[i]).ToList();

    /// <summary>The dots mirrored left-right or top-bottom inside their box.</summary>
    public static List<PointMm> Mirrored(IEnumerable<PointMm> points, bool horizontal) =>
        points.Select(p => horizontal ? p with { X = 1 - p.X } : p with { Y = 1 - p.Y }).ToList();
}
