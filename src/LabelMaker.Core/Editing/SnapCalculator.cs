using LabelMaker.Core.Units;

namespace LabelMaker.Core.Editing;

/// <summary>A line an object edge/centre snapped to, for the on-canvas snap indicator.</summary>
/// <param name="Vertical">True for a vertical line (an X position), false for a horizontal line (a Y position).</param>
/// <param name="PositionMm">Line position in document millimetres.</param>
public readonly record struct SnapLine(bool Vertical, double PositionMm);

public sealed class SnapResult
{
    /// <summary>Millimetre offset to add to the moving rectangle to land on the nearest snap targets.</summary>
    public double Dx { get; init; }

    public double Dy { get; init; }

    public IReadOnlyList<SnapLine> Lines { get; init; } = Array.Empty<SnapLine>();

    public bool Snapped => Lines.Count > 0;

    public static readonly SnapResult None = new();
}

/// <summary>
/// Pure geometry for snapping a dragged object to guides, other objects and the
/// label edges (spec §2 "snap-to-object", "smart alignment guides", "guides").
/// Screen-independent — the caller converts a pixel tolerance to millimetres.
/// </summary>
public static class SnapCalculator
{
    /// <summary>
    /// Snap a moving rectangle. <paramref name="verticalTargets"/> are candidate X
    /// positions (guides, label left/centre/right, other objects' left/centre/right);
    /// <paramref name="horizontalTargets"/> are candidate Y positions. The moving
    /// rectangle's left / centre / right (and top / middle / bottom) are each tested
    /// against the targets; the closest match within <paramref name="toleranceMm"/>
    /// per axis wins.
    /// </summary>
    public static SnapResult SnapMove(
        RectMm moving,
        IReadOnlyList<double> verticalTargets,
        IReadOnlyList<double> horizontalTargets,
        double toleranceMm)
    {
        if (toleranceMm <= 0)
        {
            return SnapResult.None;
        }

        var lines = new List<SnapLine>(2);

        (double dx, double lineX) = BestAxis(
            new[] { moving.Left, moving.Center.X, moving.Right }, verticalTargets, toleranceMm);
        (double dy, double lineY) = BestAxis(
            new[] { moving.Top, moving.Center.Y, moving.Bottom }, horizontalTargets, toleranceMm);

        if (!double.IsNaN(lineX))
        {
            lines.Add(new SnapLine(true, lineX));
        }

        if (!double.IsNaN(lineY))
        {
            lines.Add(new SnapLine(false, lineY));
        }

        return new SnapResult
        {
            Dx = double.IsNaN(lineX) ? 0 : dx,
            Dy = double.IsNaN(lineY) ? 0 : dy,
            Lines = lines,
        };
    }

    /// <summary>Snap a single moving edge value (used while resizing). Returns the snapped value.</summary>
    public static double SnapValue(double value, IReadOnlyList<double> targets, double toleranceMm, out bool snapped)
    {
        snapped = false;
        double best = value;
        double bestDist = toleranceMm;
        foreach (double t in targets)
        {
            double d = Math.Abs(t - value);
            if (d <= bestDist)
            {
                bestDist = d;
                best = t;
                snapped = true;
            }
        }

        return best;
    }

    private static (double Delta, double Line) BestAxis(
        double[] edges, IReadOnlyList<double> targets, double toleranceMm)
    {
        double bestDelta = 0;
        double bestLine = double.NaN;
        double bestDist = toleranceMm + Measure.Epsilon;

        foreach (double edge in edges)
        {
            foreach (double target in targets)
            {
                double dist = Math.Abs(target - edge);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestDelta = target - edge;
                    bestLine = target;
                }
            }
        }

        return (bestDelta, bestLine);
    }
}
