using FluentAssertions;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.Tests.Editing;

public sealed class CommandStackTests
{
    [Fact]
    public void Execute_then_undo_then_redo()
    {
        var doc = new LabelDocument();
        var stack = new CommandStack();
        var obj = new TextObject { Content = RichTextContent.FromPlainText("hi") };

        stack.Execute(new AddObjectCommand(doc, obj));
        doc.Objects.Should().ContainSingle();

        stack.Undo();
        doc.Objects.Should().BeEmpty();

        stack.Redo();
        doc.Objects.Should().ContainSingle();
    }

    [Fact]
    public void Transform_commands_merge_within_the_window()
    {
        var doc = new LabelDocument();
        var obj = new ShapeObject { Bounds = new RectMm(0, 0, 10, 10) };
        doc.Objects.Add(obj);
        var stack = new CommandStack { MergeWindow = TimeSpan.FromSeconds(5) };

        stack.Execute(new TransformObjectCommand(obj, new RectMm(1, 1, 10, 10), 0));
        stack.Execute(new TransformObjectCommand(obj, new RectMm(2, 2, 10, 10), 0));
        stack.Execute(new TransformObjectCommand(obj, new RectMm(3, 3, 10, 10), 0));

        obj.Bounds.X.Should().Be(3);
        stack.Undo(); // one merged step back to the original
        obj.Bounds.X.Should().Be(0);
        stack.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void New_command_clears_the_redo_stack()
    {
        var doc = new LabelDocument();
        var stack = new CommandStack();
        stack.Execute(new AddObjectCommand(doc, new ShapeObject()));
        stack.Undo();
        stack.CanRedo.Should().BeTrue();

        stack.Execute(new AddObjectCommand(doc, new TextObject()));
        stack.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void History_is_capacity_bounded()
    {
        var doc = new LabelDocument();
        var stack = new CommandStack(capacity: 5) { MergeWindow = TimeSpan.Zero };
        for (int i = 0; i < 20; i++)
        {
            stack.Execute(new AddObjectCommand(doc, new ShapeObject()));
        }

        int undone = 0;
        while (stack.CanUndo)
        {
            stack.Undo();
            undone++;
        }

        undone.Should().Be(5);
    }

    [Fact]
    public void Composite_command_is_one_undo_unit()
    {
        var doc = new LabelDocument();
        var a = new ShapeObject();
        var b = new ShapeObject();
        var stack = new CommandStack();

        stack.Execute(new CompositeCommand("Add pair", new IUndoableCommand[]
        {
            new AddObjectCommand(doc, a),
            new AddObjectCommand(doc, b),
        }));

        doc.Objects.Should().HaveCount(2);
        stack.Undo();
        doc.Objects.Should().BeEmpty();
    }

    [Fact]
    public void Going_to_a_step_undoes_or_redoes_everything_in_between_with_one_change()
    {
        var doc = new LabelDocument();
        var stack = new CommandStack { MergeWindow = TimeSpan.Zero };
        TextObject[] objects = Enumerable.Range(1, 4)
            .Select(i => new TextObject { Name = $"T{i}", Content = RichTextContent.FromPlainText($"{i}") }).ToArray();
        foreach (TextObject obj in objects)
        {
            stack.Execute(new AddObjectCommand(doc, obj));
        }

        int changes = 0;
        stack.Changed += (_, _) => changes++;

        stack.GoTo(1);
        doc.Objects.Should().Equal(objects[0]);
        stack.DoneCount.Should().Be(1);
        stack.RedoLabels.Should().HaveCount(3, "the undone steps stay, ready to redo");
        changes.Should().Be(1);

        stack.GoTo(3);
        doc.Objects.Should().Equal(objects[0], objects[1], objects[2]);
        stack.UndoLabels.Should().HaveCount(3);
        stack.RedoLabels.Should().ContainSingle();

        stack.GoTo(0);
        doc.Objects.Should().BeEmpty();
        stack.GoTo(99);
        doc.Objects.Should().HaveCount(4, "a step past the end stops at the last one");
        changes.Should().Be(4);

        stack.GoTo(4);
        changes.Should().Be(4, "going to the step already reached changes nothing");
    }
}
