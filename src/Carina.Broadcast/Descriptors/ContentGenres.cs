using System.Diagnostics.CodeAnalysis;

namespace Carina.Broadcast.Descriptors;

public sealed record ContentGenre(int Kind, int Sort, int UserKind, int UserSort);

public sealed record ContentGenres(IReadOnlyList<ContentGenre> Genres)
{
    private const int PairSize = 2;

    public static bool TryRead(Descriptor descriptor, [NotNullWhen(true)] out ContentGenres? genres)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        genres = null;

        if (descriptor.Tag != DescriptorTags.Content || descriptor.Payload.Length % PairSize != 0)
        {
            return false;
        }

        var payload = descriptor.Payload.Span;
        var found = new List<ContentGenre>(payload.Length / PairSize);

        for (var at = 0; at + PairSize <= payload.Length; at += PairSize)
        {
            found.Add(new ContentGenre(
                payload[at] >> 4,
                payload[at] & 0x0F,
                payload[at + 1] >> 4,
                payload[at + 1] & 0x0F));
        }

        genres = new ContentGenres(found);

        return true;
    }
}
