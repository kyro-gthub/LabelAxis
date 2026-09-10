namespace LabelMaker.Core.Model.Objects;

public enum ShapeKind
{
    Rectangle = 0,
    RoundRectangle = 1,
    Ellipse = 2,
    Line = 3,
    Arrow = 4,

    /// <summary>Regular N-gon or N-point star (see <see cref="ShapeObject.PolygonPoints"/> / <see cref="ShapeObject.StarInnerRatio"/>).</summary>
    Polygon = 5,
}

public enum DashStyle
{
    Solid = 0,
    Dash = 1,
    Dot = 2,
    DashDot = 3,
}

/// <summary>A vector shape. Stays a shape — never rasterised while editing.</summary>
public sealed class ShapeObject : LabelObject
{
    public override ObjectKind Kind => ObjectKind.Shape;

    public ShapeKind Shape { get; set; } = ShapeKind.Rectangle;

    public ColorRgba Fill { get; set; } = ColorRgba.Transparent;
    public ColorRgba Stroke { get; set; } = ColorRgba.Black;

    /// <summary>Stroke thickness in millimetres.</summary>
    public double StrokeWidthMm { get; set; } = 0.25;

    public DashStyle DashStyle { get; set; } = DashStyle.Solid;

    /// <summary>Corner radius (mm) for <see cref="ShapeKind.RoundRectangle"/>.</summary>
    public double CornerRadiusMm { get; set; } = 2.0;

    /// <summary>Vertex count for <see cref="ShapeKind.Polygon"/> (3..64).</summary>
    public int PolygonPoints { get; set; } = 5;

    /// <summary>
    /// When &gt; 0 and &lt; 1 the polygon is drawn as a star with the inner radius
    /// at this fraction of the outer radius.
    /// </summary>
    public double StarInnerRatio { get; set; }

    /// <summary>Draw an arrow head at the end point for <see cref="ShapeKind.Arrow"/>.</summary>
    public double ArrowHeadSizeMm { get; set; } = 3.0;

    public override IEnumerable<string> GetSearchableStrings() => Array.Empty<string>();

    public override LabelObject Clone()
    {
        var copy = new ShapeObject
        {
            Shape = Shape,
            Fill = Fill,
            Stroke = Stroke,
            StrokeWidthMm = StrokeWidthMm,
            DashStyle = DashStyle,
            CornerRadiusMm = CornerRadiusMm,
            PolygonPoints = PolygonPoints,
            StarInnerRatio = StarInnerRatio,
            ArrowHeadSizeMm = ArrowHeadSizeMm,
        };
        CopyBaseTo(copy);
        return copy;
    }
}
