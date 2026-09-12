using LabelMaker.Core.Units;

namespace LabelMaker.Core.Model;

/// <summary>
/// A 2D affine matrix in column-vector form: x' = A·x + C·y + E, y' = B·x + D·y + F.
/// The same six numbers are what WPF's Matrix, PdfSharp's XMatrix and SVG's
/// <c>matrix(a b c d e f)</c> expect, in that order.
/// </summary>
public readonly record struct Affine(double A, double B, double C, double D, double E, double F)
{
    public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

    public PointMm Apply(PointMm p) => new((A * p.X) + (C * p.Y) + E, (B * p.X) + (D * p.Y) + F);

    /// <summary>The linear part only (no translation) applied to a vector.</summary>
    public PointMm ApplyVector(double x, double y) => new((A * x) + (C * y), (B * x) + (D * y));

    public double Determinant => (A * D) - (B * C);

    public Affine Inverse()
    {
        double det = Determinant;
        if (Math.Abs(det) < 1e-12)
        {
            return Identity;
        }

        double ia = D / det, ib = -B / det, ic = -C / det, id = A / det;
        return new Affine(ia, ib, ic, id, -((ia * E) + (ic * F)), -((ib * E) + (id * F)));
    }

    /// <summary>this ∘ other: apply <paramref name="other"/> first, then this.</summary>
    public Affine Multiply(Affine other) => new(
        (A * other.A) + (C * other.B),
        (B * other.A) + (D * other.B),
        (A * other.C) + (C * other.D),
        (B * other.C) + (D * other.D),
        (A * other.E) + (C * other.F) + E,
        (B * other.E) + (D * other.F) + F);
}

/// <summary>
/// The one definition of where an object's corners actually are once rotation and
/// skew are applied. Rendering, hit-testing, handles, snapping and alignment all go
/// through here so they can never disagree about an object's shape.
///
/// Local space is the object's un-transformed box centred on the origin. The
/// object matrix is: translate(centre) · rotate · skew.
/// </summary>
public static class ObjectGeometry
{
    public const double MaxSkewDegrees = 75;

    public static bool IsAxisAligned(LabelObject obj) =>
        Math.Abs(obj.RotationDegrees % 360) < 1e-9 && Math.Abs(obj.SkewXDegrees) < 1e-9 && Math.Abs(obj.SkewYDegrees) < 1e-9;

    public static bool HasSkew(LabelObject obj) =>
        Math.Abs(obj.SkewXDegrees) > 1e-9 || Math.Abs(obj.SkewYDegrees) > 1e-9;

    /// <summary>Rotation · skew, without the move to the object's centre.</summary>
    public static Affine Linear(double rotationDegrees, double skewXDegrees, double skewYDegrees)
    {
        double r = rotationDegrees * Math.PI / 180.0;
        double cos = Math.Cos(r), sin = Math.Sin(r);
        double tx = Math.Tan(Math.Clamp(skewXDegrees, -MaxSkewDegrees, MaxSkewDegrees) * Math.PI / 180.0);
        double ty = Math.Tan(Math.Clamp(skewYDegrees, -MaxSkewDegrees, MaxSkewDegrees) * Math.PI / 180.0);

        var rotate = new Affine(cos, sin, -sin, cos, 0, 0);
        var skew = new Affine(1, ty, tx, 1, 0, 0);
        return rotate.Multiply(skew);
    }

    public static Affine Linear(LabelObject obj) => Linear(obj.RotationDegrees, obj.SkewXDegrees, obj.SkewYDegrees);

    /// <summary>Local (centre-origin) to document millimetres.</summary>
    public static Affine Matrix(LabelObject obj)
    {
        Affine lin = Linear(obj);
        PointMm c = obj.Bounds.Center;
        return lin with { E = c.X, F = c.Y };
    }

    /// <summary>Top-left, top-right, bottom-right, bottom-left in document millimetres.</summary>
    public static PointMm[] Corners(LabelObject obj)
    {
        Affine m = Matrix(obj);
        double hw = obj.Bounds.Width / 2.0, hh = obj.Bounds.Height / 2.0;
        return new[]
        {
            m.Apply(new PointMm(-hw, -hh)),
            m.Apply(new PointMm(hw, -hh)),
            m.Apply(new PointMm(hw, hh)),
            m.Apply(new PointMm(-hw, hh)),
        };
    }

    /// <summary>Axis-aligned box around the transformed object.</summary>
    public static RectMm Bounds(LabelObject obj)
    {
        if (IsAxisAligned(obj))
        {
            return obj.Bounds;
        }

        PointMm[] c = Corners(obj);
        double minX = c.Min(p => p.X), maxX = c.Max(p => p.X);
        double minY = c.Min(p => p.Y), maxY = c.Max(p => p.Y);
        return RectMm.FromLtrb(minX, minY, maxX, maxY);
    }

    /// <summary>Document point to the object's local (centre-origin, un-transformed) space.</summary>
    public static PointMm ToLocal(LabelObject obj, PointMm world) => Matrix(obj).Inverse().Apply(world);

    public static PointMm ToWorld(LabelObject obj, PointMm local) => Matrix(obj).Apply(local);

    /// <summary>True when the point is inside the transformed shape, grown by <paramref name="toleranceMm"/>.</summary>
    public static bool Contains(LabelObject obj, PointMm world, double toleranceMm = 0)
    {
        PointMm p = ToLocal(obj, world);
        double hw = (obj.Bounds.Width / 2.0) + toleranceMm;
        double hh = (obj.Bounds.Height / 2.0) + toleranceMm;
        return Math.Abs(p.X) <= hw && Math.Abs(p.Y) <= hh;
    }

    /// <summary>True when the transformed shape overlaps the rectangle (used by drag-to-select).</summary>
    public static bool Intersects(LabelObject obj, RectMm rect)
    {
        RectMm aabb = Bounds(obj);
        if (aabb.Right < rect.Left || aabb.Left > rect.Right || aabb.Bottom < rect.Top || aabb.Top > rect.Bottom)
        {
            return false;
        }

        if (IsAxisAligned(obj))
        {
            return true;
        }

        // Separating axis test: the object's corners against the rect, and the rect's corners against the object.
        PointMm[] corners = Corners(obj);
        if (corners.Any(c => rect.Contains(c)))
        {
            return true;
        }

        var rectCorners = new[]
        {
            new PointMm(rect.Left, rect.Top), new PointMm(rect.Right, rect.Top),
            new PointMm(rect.Right, rect.Bottom), new PointMm(rect.Left, rect.Bottom),
        };
        if (rectCorners.Any(c => Contains(obj, c)))
        {
            return true;
        }

        // Edges crossing without any corner inside.
        for (int i = 0; i < 4; i++)
        {
            PointMm a1 = corners[i], a2 = corners[(i + 1) % 4];
            for (int j = 0; j < 4; j++)
            {
                if (SegmentsIntersect(a1, a2, rectCorners[j], rectCorners[(j + 1) % 4]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(PointMm p1, PointMm p2, PointMm q1, PointMm q2)
    {
        static double Cross(PointMm o, PointMm a, PointMm b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
        double d1 = Cross(q1, q2, p1), d2 = Cross(q1, q2, p2), d3 = Cross(p1, p2, q1), d4 = Cross(p1, p2, q2);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }
}
