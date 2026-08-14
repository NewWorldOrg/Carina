namespace Carina.Broadcast.Descriptors;

public static class DescriptorLoop
{
    private static readonly IReadOnlyList<Descriptor> Nothing = [];

    public static bool TryRead(ReadOnlyMemory<byte> loop, out IReadOnlyList<Descriptor> descriptors)
    {
        var found = new List<Descriptor>();
        var at = 0;

        while (at < loop.Length)
        {
            if (loop.Length - at < Descriptor.HeaderSize)
            {
                descriptors = Nothing;

                return false;
            }

            var span = loop.Span;
            var tag = span[at];
            var length = span[at + 1];
            var start = at + Descriptor.HeaderSize;

            if (start + length > loop.Length)
            {
                descriptors = Nothing;

                return false;
            }

            found.Add(new Descriptor(tag, loop.Slice(start, length)));
            at = start + length;
        }

        descriptors = found;

        return true;
    }

    public static Descriptor? WithTag(this IReadOnlyList<Descriptor> descriptors, int tag)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        foreach (var descriptor in descriptors)
        {
            if (descriptor.Tag == tag)
            {
                return descriptor;
            }
        }

        return null;
    }
}
