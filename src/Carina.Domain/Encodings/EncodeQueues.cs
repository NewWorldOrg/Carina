namespace Carina.Domain.Encodings;

/// <summary>
/// Whether the queue gives way to someone watching before it starts another job. There is one card
/// in the machine, and a picture being watched cannot wait behind a job: while any transcoder is up
/// for a viewer, a job that would go to the card is left in the queue and looked at again later. A
/// job that would go to the processor is not held back, because it takes only cores, of which this
/// machine has several and out of whose way the run is niced. Nor is a job that would go to a card
/// this machine cannot use, since it will swerve to the processor anyway.
/// <para>
/// This is asked before a job is claimed and never again once one runs: a run stopped part way
/// throws away everything it has encoded so far, which costs more than the wait it would save.
/// </para>
/// </summary>
public static class EncodeQueues
{
    public static bool YieldsToAViewer(EncodeEncoder prefer, bool cardIsUsable, int watching)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(watching);

        return EncodeShapes.Named(prefer) is EncodeEncoder.Vaapi && cardIsUsable && watching > 0;
    }
}
