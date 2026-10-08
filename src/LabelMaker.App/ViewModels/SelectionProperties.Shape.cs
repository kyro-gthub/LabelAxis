using System.Collections.ObjectModel;
using System.Windows.Media;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Services;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Units;

namespace LabelMaker.App.ViewModels;

/// <summary>The Properties panel for a shape: kind, outline, fill and gradient, and a custom shape's dots.</summary>
public sealed partial class SelectionProperties
{
    private static readonly IReadOnlyList<ShapeKind> AllShapeKinds = Enum.GetValues<ShapeKind>();

    // A custom shape is drawn dot by dot, so it is only offered for a shape that already is one.
    private static readonly IReadOnlyList<ShapeKind> DrawableShapeKinds = AllShapeKinds.Where(k => k != ShapeKind.Custom).ToList();

    public IReadOnlyList<ShapeKind> ShapeKinds => IsCustomShape ? AllShapeKinds : DrawableShapeKinds;

    public static IReadOnlyList<LabelMaker.Core.Model.Objects.DashStyle> DashStyles { get; } =
        Enum.GetValues<LabelMaker.Core.Model.Objects.DashStyle>();

    /// <summary>
    /// A compact palette for one-click fill/stroke colours. ColorRgba is #RRGGBB(AA),
    /// alpha last — "#00000000" is the transparent entry, not a black one.
    /// </summary>
    /// <summary>The palette offered for every colour, shared with the colour picker.</summary>
    public static IReadOnlyList<string> Swatches { get; } = Controls.ColorPalette.Colors.Select(c => c.Hex).ToList();

    public ShapeKind ShapeKindValue
    {
        get => (Obj as ShapeObject)?.Shape ?? ShapeKind.Rectangle;
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(ShapeKindValue), () => s.Shape, v => s.Shape = v, value);
                OnPropertyChanged(nameof(IsPolygon));
                OnPropertyChanged(nameof(IsRoundRect));
            }
        }
    }

    public bool IsPolygon => (Obj as ShapeObject)?.Shape == ShapeKind.Polygon;

    public bool IsRoundRect => (Obj as ShapeObject)?.Shape == ShapeKind.RoundRectangle;

    public bool IsCustomShape => (Obj as ShapeObject)?.Shape == ShapeKind.Custom;

    /// <summary>A custom shape joined up and filled, or an open line through its dots.</summary>
    public bool ClosedPath
    {
        get => (Obj as ShapeObject)?.ClosedPath ?? true;
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(ClosedPath), () => s.ClosedPath, v => s.ClosedPath = v, value);
            }
        }
    }

    public int CustomDotCount => (Obj as ShapeObject)?.CustomPoints.Count ?? 0;

    /// <summary>
    /// Every dot of a custom shape curved (true), every one a sharp corner (false), or some of
    /// each (null, shown as a part-ticked box). Single dots are switched while editing the dots.
    /// </summary>
    public bool? AllDotsCurved
    {
        get
        {
            if (Obj is not ShapeObject s || s.CustomPoints.Count == 0)
            {
                return false;
            }

            List<bool> flags = CustomShape.CurvedFlags(s.CurvedPoints, s.CustomPoints.Count);
            return flags.All(c => c) ? true : flags.Any(c => c) ? null : false;
        }
        set
        {
            if (Obj is ShapeObject s && value is { } curved)
            {
                Set(nameof(AllDotsCurved), () => s.CurvedPoints, v => s.CurvedPoints = v,
                    curved ? Enumerable.Repeat(true, s.CustomPoints.Count).ToList() : new List<bool>());
            }
        }
    }

    public LabelMaker.Core.Model.Objects.DashStyle DashStyleValue
    {
        get => (Obj as ShapeObject)?.DashStyle ?? LabelMaker.Core.Model.Objects.DashStyle.Solid;
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(DashStyleValue), () => s.DashStyle, v => s.DashStyle = v, value);
            }
        }
    }

    public double PolygonPoints
    {
        get => (Obj as ShapeObject)?.PolygonPoints ?? 5;
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(PolygonPoints), () => s.PolygonPoints, v => s.PolygonPoints = v, (int)Math.Clamp(value, 3, 64));
            }
        }
    }

    /// <summary>0 = plain N-gon; 0.1..0.9 turns it into a star with that inner radius.</summary>
    public double StarInnerRatio
    {
        get => Round((Obj as ShapeObject)?.StarInnerRatio ?? 0);
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(StarInnerRatio), () => s.StarInnerRatio, v => s.StarInnerRatio = v, Math.Clamp(value, 0, 0.95));
            }
        }
    }

    public double ArrowHeadSizeMm
    {
        get => Out((Obj as ShapeObject)?.ArrowHeadSizeMm ?? 3);
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(ArrowHeadSizeMm), () => s.ArrowHeadSizeMm, v => s.ArrowHeadSizeMm = Math.Clamp(v, 0.5, 50), In(value));
            }
        }
    }

    public string FillHex
    {
        get => (Obj as ShapeObject)?.Fill.ToHex() ?? "#00000000";
        set
        {
            if (Obj is ShapeObject s && ColorRgba.TryParse(value, out ColorRgba c))
            {
                Set(nameof(FillHex), () => s.Fill, v => s.Fill = v, c);
                OnPropertyChanged(nameof(FillBrush));
                OnPropertyChanged(nameof(FillAlpha));
            }
        }
    }

    public string StrokeHex
    {
        get => (Obj as ShapeObject)?.Stroke.ToHex() ?? "#000000";
        set
        {
            if (Obj is ShapeObject s && ColorRgba.TryParse(value, out ColorRgba c))
            {
                Set(nameof(StrokeHex), () => s.Stroke, v => s.Stroke = v, c);
                OnPropertyChanged(nameof(StrokeBrush));
            }
        }
    }

    /// <summary>Live preview swatches for the two colour buttons.</summary>
    public Brush FillBrush => Obj is ShapeObject { GradientTo: { } to } shape
        ? ToBrush(shape.Fill, to, shape.GradientAngleDegrees)
        : ToBrush((Obj as ShapeObject)?.Fill ?? ColorRgba.Transparent);

    public Brush StrokeBrush => ToBrush((Obj as ShapeObject)?.Stroke ?? ColorRgba.Black);

    /// <summary>Fill opacity as 0..100, so it can sit on a slider.</summary>
    public double FillAlpha
    {
        get => Math.Round(((Obj as ShapeObject)?.Fill.A ?? 0) / 255.0 * 100);
        set
        {
            if (Obj is ShapeObject s)
            {
                ColorRgba c = s.Fill;
                var next = new ColorRgba(c.R, c.G, c.B, (byte)Math.Clamp(Math.Round(value / 100 * 255), 0, 255));
                Set(nameof(FillAlpha), () => s.Fill, v => s.Fill = v, next);
                OnPropertyChanged(nameof(FillBrush));
                OnPropertyChanged(nameof(FillHex));
            }
        }
    }

    /// <summary>Turns the flat fill into a two-stop linear gradient and back.</summary>
    public bool UseGradient
    {
        get => (Obj as ShapeObject)?.HasGradient == true;
        set
        {
            if (Obj is not ShapeObject s)
            {
                return;
            }

            ColorRgba? next = value
                ? (s.GradientTo ?? Lighten(s.Fill.A == 0 ? new ColorRgba(0x2C, 0x6F, 0xE8) : s.Fill))
                : null;

            if (value && s.Fill.IsTransparent)
            {
                // A gradient from "nothing" is invisible; start from the accent colour.
                Set(nameof(FillHex), () => s.Fill, v => s.Fill = v, new ColorRgba(0x2C, 0x6F, 0xE8));
            }

            Set(nameof(UseGradient), () => s.GradientTo, v => s.GradientTo = v, next);
            OnPropertyChanged(nameof(GradientToHex));
            OnPropertyChanged(nameof(GradientPreview));
            OnPropertyChanged(nameof(FillBrush));
        }
    }

    public string GradientToHex
    {
        get => ((Obj as ShapeObject)?.GradientTo ?? ColorRgba.White).ToHex();
        set
        {
            if (Obj is ShapeObject s && ColorRgba.TryParse(value, out ColorRgba c))
            {
                Set(nameof(GradientToHex), () => s.GradientTo, v => s.GradientTo = v, c);
                OnPropertyChanged(nameof(GradientPreview));
            }
        }
    }

    public double GradientAngle
    {
        get => Math.Round((Obj as ShapeObject)?.GradientAngleDegrees ?? 90);
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(GradientAngle), () => s.GradientAngleDegrees, v => s.GradientAngleDegrees = v,
                    Math.Round(Math.Clamp(value, 0, 360)));
                OnPropertyChanged(nameof(GradientPreview));
            }
        }
    }

    /// <summary>Live preview of the gradient exactly as the renderer will draw it.</summary>
    public Brush GradientPreview
    {
        get
        {
            if (Obj is not ShapeObject { GradientTo: { } to } s)
            {
                return FillBrush;
            }

            double radians = s.GradientAngleDegrees * Math.PI / 180.0;
            double dx = Math.Cos(radians) / 2.0;
            double dy = Math.Sin(radians) / 2.0;
            return new LinearGradientBrush(
                Color.FromArgb(s.Fill.A, s.Fill.R, s.Fill.G, s.Fill.B),
                Color.FromArgb(to.A, to.R, to.G, to.B),
                new System.Windows.Point(0.5 - dx, 0.5 - dy),
                new System.Windows.Point(0.5 + dx, 0.5 + dy));
        }
    }

    private static ColorRgba Lighten(ColorRgba c) => new(
        (byte)Math.Min(255, c.R + 70), (byte)Math.Min(255, c.G + 70), (byte)Math.Min(255, c.B + 70), c.A);

    public double StrokeWidthMm
    {
        get => Out((Obj as ShapeObject)?.StrokeWidthMm ?? 0.25);
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(StrokeWidthMm), () => s.StrokeWidthMm, v => s.StrokeWidthMm = Math.Clamp(v, 0, 20), In(value));
            }
        }
    }

    public double CornerRadiusMm
    {
        get => Out((Obj as ShapeObject)?.CornerRadiusMm ?? 0);
        set
        {
            if (Obj is ShapeObject s)
            {
                Set(nameof(CornerRadiusMm), () => s.CornerRadiusMm, v => s.CornerRadiusMm = Math.Max(0, v), In(value));
            }
        }
    }
}
