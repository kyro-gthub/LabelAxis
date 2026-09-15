using FluentAssertions;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using Xunit;

namespace LabelMaker.Tests.Editing;

public sealed class ObjectStackingTests
{
    private static List<LabelObject> Objects(int n) =>
        Enumerable.Range(0, n).Select(i => (LabelObject)new ShapeObject { Name = ((char)('a' + i)).ToString() }).ToList();

    private static string Names(IEnumerable<LabelObject> objects) => string.Concat(objects.Select(o => o.Name));

    [Fact]
    public void Dropping_rows_higher_in_the_front_to_back_list_brings_them_forward()
    {
        List<LabelObject> backToFront = Objects(4); // a b c d, d on top
        var frontToBack = Enumerable.Reverse(backToFront).ToList(); // d c b a

        List<LabelObject> result = ObjectStacking.MoveTo(frontToBack, new[] { backToFront[0] }, 0);

        Names(result).Should().Be("bcda");
    }

    [Fact]
    public void Several_rows_move_as_one_block_keeping_their_order()
    {
        List<LabelObject> backToFront = Objects(5); // a b c d e
        var frontToBack = Enumerable.Reverse(backToFront).ToList(); // e d c b a

        // move d and b to just behind a (the end of the front-to-back list)
        List<LabelObject> result = ObjectStacking.MoveTo(frontToBack, new[] { backToFront[3], backToFront[1] }, frontToBack.Count);

        Names(Enumerable.Reverse(result)).Should().Be("ecadb");
    }

    [Fact]
    public void Bringing_several_forward_moves_each_one_place_without_swapping_them()
    {
        List<LabelObject> o = Objects(5); // a b c d e
        Names(ObjectStacking.Step(o, new[] { o[1], o[2] }, +1)).Should().Be("adbce");
        Names(ObjectStacking.Step(o, new[] { o[3], o[4] }, +1)).Should().Be("abcde");
        Names(ObjectStacking.Step(o, new[] { o[1], o[3] }, -1)).Should().Be("badce");
        Names(ObjectStacking.Step(o, new[] { o[0], o[2] }, int.MaxValue)).Should().Be("bdeac");
        Names(ObjectStacking.Step(o, new[] { o[4], o[2] }, int.MinValue)).Should().Be("ceabd");
    }

    [Fact]
    public void Arrange_command_undoes_to_the_original_order()
    {
        var doc = new LabelDocument();
        doc.Objects.AddRange(Objects(3));
        var command = new ArrangeObjectsCommand(doc, ObjectStacking.Step(doc.Objects, new[] { doc.Objects[0] }, int.MaxValue));

        command.Do();
        Names(doc.Objects).Should().Be("bca");
        command.Undo();
        Names(doc.Objects).Should().Be("abc");
    }
}
