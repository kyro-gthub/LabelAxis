namespace LabelMaker.Core.Units;

/// <summary>A point in document millimetres.</summary>
public readonly record struct PointMm(double X, double Y)
{
    public static readonly PointMm Origin = new(0, 0);

    public PointMm Offset(double dx, double dy) => new(X + dx, Y + dy);

    public double DistanceTo(PointMm other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

/// <summary>A size in document millimetres.</summary>
public readonly record struct SizeMm(double Width, double Height)
{
    public static readonly SizeMm Empty = new(0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public double Area => Width * Height;
}

/// <summary>An axis-aligned rectangle in document millimetres.</summary>
public readonly record struct RectMm(double X, double Y, double Width, double Height)
{
    public static readonly RectMm Empty = new(0, 0, 0, 0);

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public PointMm TopLeft => new(X, Y);
    public PointMm Center => new(X + (Width / 2.0), Y + (Height / 2.0));
    public SizeMm Size => new(Width, Height);

    public static RectMm FromLtrb(double left, double top, double right, double bottom) =>
        new(left, top, right - left, bottom - top);

    public static RectMm FromCenter(PointMm center, SizeMm size) =>
        new(center.X - (size.Width / 2.0), center.Y - (size.Height / 2.0), size.Width, size.Height);

    public bool Contains(PointMm p) =>
        p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;

    public bool Contains(RectMm inner) =>
        inner.Left >= Left - Measure.Epsilon && inner.Top >= Top - Measure.Epsilon &&
        inner.Right <= Right + Measure.Epsilon && inner.Bottom <= Bottom + Measure.Epsilon;

    public bool IntersectsWith(RectMm other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    public RectMm Inflate(double dx, double dy) =>
        new(X - dx, Y - dy, Width + (2 * dx), Height + (2 * dy));

    public RectMm Deflate(double margin) => Inflate(-margin, -margin);

    public RectMm Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    public RectMm Intersect(RectMm other)
    {
        double l = Math.Max(Left, other.Left);
        double t = Math.Max(Top, other.Top);
        double r = Math.Min(Right, other.Right);
        double b = Math.Min(Bottom, other.Bottom);
        return r <= l || b <= t ? Empty : FromLtrb(l, t, r, b);
    }

    public RectMm Union(RectMm other)
    {
        double l = Math.Min(Left, other.Left);
        double t = Math.Min(Top, other.Top);
        double r = Math.Max(Right, other.Right);
        double b = Math.Max(Bottom, other.Bottom);
        return FromLtrb(l, t, r, b);
    }

    /// <summary>Axis-aligned bounds of this rectangle rotated <paramref name="degrees"/> about its centre.</summary>
    public RectMm RotatedBounds(double degrees)
    {
        if (Math.Abs(degrees % 360.0) < 1e-9)
        {
            return this;
        }

        double rad = degrees * Math.PI / 180.0;
        double cos = Math.Abs(Math.Cos(rad));
        double sin = Math.Abs(Math.Sin(rad));
        double w = (Width * cos) + (Height * sin);
        double h = (Width * sin) + (Height * cos);
        return FromCenter(Center, new SizeMm(w, h));
    }
}
