using System.Diagnostics.CodeAnalysis;

using Carina.Broadcast.Text;

namespace Carina.Broadcast.Descriptors;

public sealed record ExtendedEventItem(string Heading, string Text);

public sealed record ExtendedEventDescription(
    string Language,
    IReadOnlyList<ExtendedEventItem> Items,
    string Text)
{
    private const int HeaderSize = 5;

    public static bool TryRead(
        IReadOnlyList<Descriptor> descriptors,
        [NotNullWhen(true)] out ExtendedEventDescription? described)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        described = null;

        var carried = Ordered(descriptors);

        if (carried.Count == 0)
        {
            return false;
        }

        var language = string.Empty;
        var headings = new List<string>();
        var bodies = new List<List<ReadOnlyMemory<byte>>>();
        var text = new List<ReadOnlyMemory<byte>>();

        foreach (var descriptor in carried)
        {
            var payload = descriptor.Payload;

            if (payload.Length < HeaderSize)
            {
                return false;
            }

            if (language.Length == 0)
            {
                language = LanguageCode.Of(payload.Span.Slice(1, LanguageCode.Size));
            }

            var itemsLength = payload.Span[4];
            var afterItems = HeaderSize + itemsLength;

            if (afterItems + 1 > payload.Length)
            {
                return false;
            }

            if (!ReadItems(payload.Slice(HeaderSize, itemsLength), headings, bodies))
            {
                return false;
            }

            var textLength = payload.Span[afterItems];

            if (afterItems + 1 + textLength > payload.Length)
            {
                return false;
            }

            text.Add(payload.Slice(afterItems + 1, textLength));
        }

        described = new ExtendedEventDescription(
            language,
            [.. headings.Select((heading, at) => new ExtendedEventItem(heading, Decode(bodies[at])))],
            Decode(text));

        return true;
    }

    private static bool ReadItems(
        ReadOnlyMemory<byte> loop,
        List<string> headings,
        List<List<ReadOnlyMemory<byte>>> bodies)
    {
        var at = 0;

        while (at < loop.Length)
        {
            if (at + 1 > loop.Length)
            {
                return false;
            }

            var headingLength = loop.Span[at];
            var afterHeading = at + 1 + headingLength;

            if (afterHeading + 1 > loop.Length)
            {
                return false;
            }

            var bodyLength = loop.Span[afterHeading];

            if (afterHeading + 1 + bodyLength > loop.Length)
            {
                return false;
            }

            var body = loop.Slice(afterHeading + 1, bodyLength);

            if (headingLength == 0 && bodies.Count > 0)
            {
                bodies[^1].Add(body);
            }
            else
            {
                headings.Add(AribText.Decode(loop.Slice(at + 1, headingLength)));
                bodies.Add([body]);
            }

            at = afterHeading + 1 + bodyLength;
        }

        return true;
    }

    private static string Decode(IReadOnlyList<ReadOnlyMemory<byte>> parts)
    {
        if (parts.Count == 1)
        {
            return AribText.Decode(parts[0]);
        }

        var length = 0;

        foreach (var part in parts)
        {
            length += part.Length;
        }

        var joined = new byte[length];
        var at = 0;

        foreach (var part in parts)
        {
            part.Span.CopyTo(joined.AsSpan(at));
            at += part.Length;
        }

        return AribText.Decode(joined);
    }

    private static IReadOnlyList<Descriptor> Ordered(IReadOnlyList<Descriptor> descriptors)
    {
        var carried = new List<Descriptor>();

        foreach (var descriptor in descriptors)
        {
            if (descriptor.Tag == DescriptorTags.ExtendedEvent && descriptor.Payload.Length >= 1)
            {
                carried.Add(descriptor);
            }
        }

        carried.Sort((left, right) => (left.Payload.Span[0] >> 4).CompareTo(right.Payload.Span[0] >> 4));

        return carried;
    }
}
