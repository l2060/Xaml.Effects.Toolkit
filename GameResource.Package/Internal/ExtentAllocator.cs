namespace GameResource.Package.Internal;

internal readonly record struct Extent(long Offset, long Length)
{
    internal long End => checked(Offset + Length);
}

internal sealed class ExtentAllocator(long length, IEnumerable<Extent>? free = null)
{
    internal long Length { get; private set; } = length;
    internal List<Extent> Free { get; } = free?.ToList() ?? [];
    internal static long Align(long value) => checked((value + 7) & ~7L);

    internal void TrimTail()
    {
        if (Free.Count != 0 && Free[^1].End == Length)
        {
            Length = Free[^1].Offset;
            Free.RemoveAt(Free.Count - 1);
        }
    }

    internal Extent Allocate(long size)
    {
        size = Align(size);
        if (size == 0) return default;
        for (int i = 0; i < Free.Count; i++)
        {
            var space = Free[i];
            if (space.Length < size) continue;
            if (space.Length == size) Free.RemoveAt(i);
            else Free[i] = new(space.Offset + size, space.Length - size);
            return new(space.Offset, size);
        }
        var result = new Extent(Length, size);
        Length = result.End;
        return result;
    }

    internal void Release(Extent extent)
    {
        if (extent.Length == 0) return;
        int i = Free.BinarySearch(extent, Comparer<Extent>.Create((a, b) => a.Offset.CompareTo(b.Offset)));
        if (i < 0) i = ~i;
        if ((i > 0 && Free[i - 1].End > extent.Offset) || (i < Free.Count && extent.End > Free[i].Offset))
            throw new InvalidDataException("Overlapping free extents.");
        if (i > 0 && Free[i - 1].End == extent.Offset)
        {
            extent = new(Free[i - 1].Offset, checked(Free[i - 1].Length + extent.Length));
            Free.RemoveAt(--i);
        }
        if (i < Free.Count && extent.End == Free[i].Offset)
        {
            extent = new(extent.Offset, checked(extent.Length + Free[i].Length));
            Free.RemoveAt(i);
        }
        Free.Insert(i, extent);
    }
}
