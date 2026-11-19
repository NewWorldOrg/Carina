namespace Carina.Domain.Encodings;

public static class EncodeQueues
{
    public static bool YieldsToAViewer(EncodeEncoder prefer, bool cardIsUsable, int watching)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(watching);

        return EncodeEncoders.OnTheCard(prefer) && cardIsUsable && watching > 0;
    }
}
