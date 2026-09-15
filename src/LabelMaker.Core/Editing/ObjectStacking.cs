using LabelMaker.Core.Model;

namespace LabelMaker.Core.Editing;

/// <summary>
/// Stacking-order arithmetic for a document's objects, which are kept back to front
/// (the last object is drawn on top). Every method returns a new back-to-front order.
/// </summary>
public static class ObjectStacking
{
    /// <summary>
    /// Moves <paramref name="moving"/> as one block, keeping their order, to position
    /// <paramref name="insertAt"/> of the front-to-back list <paramref name="frontToBack"/>
    /// (0 = in front of everything, Count = behind everything), as dragging rows in an
    /// object list does.
    /// </summary>
    public static List<LabelObject> MoveTo(IReadOnlyList<LabelObject> frontToBack, IReadOnlyCollection<LabelObject> moving, int insertAt)
    {
        ArgumentNullException.ThrowIfNull(frontToBack);
        ArgumentNullException.ThrowIfNull(moving);
        var set = new HashSet<LabelObject>(moving);
        insertAt = Math.Clamp(insertAt, 0, frontToBack.Count);

        int removedBefore = frontToBack.Take(insertAt).Count(set.Contains);
        List<LabelObject> block = frontToBack.Where(set.Contains).ToList();
        List<LabelObject> rest = frontToBack.Where(o => !set.Contains(o)).ToList();
        rest.InsertRange(insertAt - removedBefore, block);
        rest.Reverse();
        return rest;
    }

    /// <summary>
    /// Brings <paramref name="selected"/> forward (<paramref name="delta"/> +1) or back (−1)
    /// one place each, or to the very front (<see cref="int.MaxValue"/>) or back
    /// (<see cref="int.MinValue"/>). Selected neighbours move together instead of swapping
    /// with each other.
    /// </summary>
    public static List<LabelObject> Step(IReadOnlyList<LabelObject> backToFront, IReadOnlyCollection<LabelObject> selected, int delta)
    {
        ArgumentNullException.ThrowIfNull(backToFront);
        ArgumentNullException.ThrowIfNull(selected);
        var set = new HashSet<LabelObject>(selected);
        var order = backToFront.ToList();

        switch (delta)
        {
            case int.MaxValue:
                return order.Where(o => !set.Contains(o)).Concat(order.Where(set.Contains)).ToList();
            case int.MinValue:
                return order.Where(set.Contains).Concat(order.Where(o => !set.Contains(o))).ToList();
            case > 0:
                for (int i = order.Count - 2; i >= 0; i--)
                {
                    if (set.Contains(order[i]) && !set.Contains(order[i + 1]))
                    {
                        (order[i], order[i + 1]) = (order[i + 1], order[i]);
                    }
                }

                return order;
            case < 0:
                for (int i = 1; i < order.Count; i++)
                {
                    if (set.Contains(order[i]) && !set.Contains(order[i - 1]))
                    {
                        (order[i], order[i - 1]) = (order[i - 1], order[i]);
                    }
                }

                return order;
            default:
                return order;
        }
    }
}
