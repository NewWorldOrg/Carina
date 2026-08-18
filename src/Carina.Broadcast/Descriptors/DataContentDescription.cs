using System.Diagnostics.CodeAnalysis;

namespace Carina.Broadcast.Descriptors;

public sealed record DataContentDescription(int DataComponentId)
{
    public const int Captions = 0x0008;

    public const int Superimpose = 0x0012;

    private const int HeaderSize = 2;

    public bool CarriesCaptions => DataComponentId == Captions;

    public bool CarriesSuperimpose => DataComponentId == Superimpose;

    public static bool TryRead(Descriptor descriptor, [NotNullWhen(true)] out DataContentDescription? described)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        described = null;

        if (descriptor.Tag != DescriptorTags.DataContent || descriptor.Payload.Length < HeaderSize)
        {
            return false;
        }

        ReadOnlySpan<byte> payload = descriptor.Payload.Span;

        described = new DataContentDescription((payload[0] << 8) | payload[1]);

        return true;
    }
}
