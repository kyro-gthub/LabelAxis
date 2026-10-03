using FluentAssertions;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Rendering;
using LabelMaker.Core.Units;
using LabelMaker.Core.Variables;
using LabelMaker.Tests.Support;
using Xunit;

namespace LabelMaker.Tests.Model;

public sealed class CustomShapeTests
{
    private static readonly PointMm[] House =
    {
        new(10, 20), new(20, 10), new(30, 20), new(30, 40), new(10, 40),
    };

    [Fact]
    public void Dots_become_a_shape_boxed_around_them_with_each_dot_as_a_fraction_of_the_box()
    {
        ShapeObject s = CustomShape.Create(House);

        s.Shape.Should().Be(ShapeKind.Custom);
        s.Bounds.Should().Be(new RectMm(10, 10, 20, 30));
        s.ClosedPath.Should().BeTrue();
        s.CustomPoints[0].Should().Be(new PointMm(0, 1.0 / 3.0));
        s.CustomPoints[1].Should().Be(new PointMm(0.5, 0));
        CustomShape.WorldPoints(s).Select(p => new PointMm(Math.Round(p.X, 6), Math.Round(p.Y, 6))).Should().Equal(House);
    }

    [Fact]
    public void Two_dots_make_an_open_line_and_a_level_line_still_gets_a_box()
    {
        ShapeObject s = CustomShape.Create(new[] { new PointMm(5, 8), new PointMm(25, 8) });

        s.ClosedPath.Should().BeFalse();
        s.Bounds.Width.Should().Be(20);
        s.Bounds.Height.Should().BeApproximately(CustomShape.MinSizeMm, 1e-9);
        s.Bounds.Center.Y.Should().BeApproximately(8, 1e-9);
    }

    [Fact]
    public void Resizing_the_box_stretches_the_shape()
    {
        ShapeObject s = CustomShape.Create(House);
        s.Bounds = new RectMm(0, 0, 40, 60);

        CustomShape.PointsIn(s, s.Bounds)[1].Should().Be(new PointMm(20, 0));
        CustomShape.PointsIn(s, s.Bounds)[3].Should().Be(new PointMm(40, 60));
    }

    [Fact]
    public void Moving_a_dot_of_a_rotated_shape_keeps_it_rotated_and_the_other_dots_still()
    {
        ShapeObject s = CustomShape.Create(House);
        s.RotationDegrees = 30;
        List<PointMm> world = CustomShape.WorldPoints(s);

        // Pull the roof's tip further out.
        world[1] = new PointMm(world[1].X + 3, world[1].Y - 5);
        (RectMm bounds, List<PointMm> points) = CustomShape.Refit(s, world);
        s.Bounds = bounds;
        s.CustomPoints = points;

        s.RotationDegrees.Should().Be(30);
        List<PointMm> after = CustomShape.WorldPoints(s);
        for (int i = 0; i < world.Count; i++)
        {
            after[i].X.Should().BeApproximately(world[i].X, 1e-6);
            after[i].Y.Should().BeApproximately(world[i].Y, 1e-6);
        }
    }

    [Fact]
    public void Flipping_mirrors_the_dots_inside_the_box()
    {
        var points = new List<PointMm> { new(0.2, 0.1), new(1, 0.9) };

        CustomShape.Mirrored(points, horizontal: true).Should().Equal(new PointMm(0.8, 0.1), new PointMm(0, 0.9));
        CustomShape.Mirrored(points, horizontal: false)[0].Y.Should().BeApproximately(0.9, 1e-12);
    }

    [Fact]
    public void A_closed_shape_is_drawn_filled_and_an_open_one_as_lines_and_both_survive_a_copy()
    {
        var doc = new LabelDocument { SizeMm = new SizeMm(60, 60) };
        ShapeObject closed = CustomShape.Create(House);
        ShapeObject open = CustomShape.Create(House, closed: false);
        doc.Objects.Add(closed);
        doc.Objects.Add(open);

        var surface = new RecordingSurface();
        new LabelRenderer().Render(doc, new ResolveContext(), surface);

        surface.Ops.Should().Contain("polygon(5)");
        surface.Ops.Count(o => o == "line").Should().Be(4);

        var copy = (ShapeObject)closed.Clone();
        copy.CustomPoints.Should().Equal(closed.CustomPoints).And.NotBeSameAs(closed.CustomPoints);
        ((ShapeObject)open.Clone()).ClosedPath.Should().BeFalse();
    }
}

public sealed class CustomShapeSaveTests
{
    [Fact]
    public void A_custom_shape_and_a_page_copies_number_survive_saving_and_opening_the_project()
    {
        var template = LabelMaker.Core.Formtec.TemplateStore.CreateWithBuiltIns().Get("formtec-gs-1065")!;
        var project = ProjectModel.CreateNew(template, "Shapes");
        ShapeObject shape = CustomShape.Create(new[] { new PointMm(1, 1), new PointMm(9, 2), new PointMm(5, 8) });
        project.MasterLabel.Objects.Add(shape);
        project.MasterLabel.PrintCopies = 6;
        var image = new ImageObject { BorderWidthMm = 1.2, BorderLines = ImageBorderLines.Double, Bounds = new RectMm(0, 0, 5, 5) };
        project.MasterLabel.Objects.Add(image);

        string path = Path.Combine(Path.GetTempPath(), $"lm-{Guid.NewGuid():N}.labelproj");
        try
        {
            LabelMaker.Core.Serialization.ProjectFile.Save(new LabelMaker.Core.Serialization.ProjectDocument { Project = project }, path);
            ProjectModel loaded = LabelMaker.Core.Serialization.ProjectFile.Load(path).Project;

            var back = loaded.MasterLabel.Objects.OfType<ShapeObject>().Single();
            back.Shape.Should().Be(ShapeKind.Custom);
            back.CustomPoints.Should().Equal(shape.CustomPoints);
            back.ClosedPath.Should().BeTrue();
            loaded.MasterLabel.PrintCopies.Should().Be(6);
            var img = loaded.MasterLabel.Objects.OfType<ImageObject>().Single();
            img.BorderWidthMm.Should().Be(1.2);
            img.BorderLines.Should().Be(ImageBorderLines.Double);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }
}
