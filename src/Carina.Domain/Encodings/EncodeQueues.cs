namespace Carina.Domain.Encodings;

/// <summary>
/// Whether the queue gives way to someone watching before it starts another job.
/// </summary>
/// <remarks>
/// While any transcoder is up for a viewer, a job that would go to the card is left in the queue and
/// looked at again later. A job that would go to the processor, or to a card this machine cannot
/// use, is not held back. This is asked before a job is claimed and never once it runs.
/// </remarks>
public static class EncodeQueues
{
    public static bool YieldsToAViewer(EncodeEncoder prefer, bool cardIsUsable, int watching)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(watching);

        return EncodeShapes.Named(prefer) is EncodeEncoder.Vaapi && cardIsUsable && watching > 0;
    }
}
