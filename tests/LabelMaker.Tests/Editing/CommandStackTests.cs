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
}
