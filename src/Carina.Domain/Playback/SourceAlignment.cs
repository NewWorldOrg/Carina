using Carina.Domain.Encodings;

namespace Carina.Domain.Playback;

public sealed record SourceAlignment
{
    public static readonly SourceAlignment Unplaced = new(null, null, null);

    private SourceAlignment(SourceZero? zero, EncodeJobId? artefact, TimeSpan? artefactBegan)
    {
        Zero = zero;
        Artefact = artefact;
        ArtefactBegan = artefactBegan;
    }

    public SourceZero? Zero { get; }

    public EncodeJobId? Artefact { get; }

    public TimeSpan? ArtefactBegan { get; }

    public static SourceAlignment At(SourceZero zero)
    {
        ArgumentNullException.ThrowIfNull(zero);

        return new SourceAlignment(zero, null, null);
    }

    public static SourceAlignment ClocksDisagree(EncodeJobId artefact, TimeSpan artefactBegan)
    {
        ArgumentNullException.ThrowIfNull(artefact);

        return new SourceAlignment(null, artefact, artefactBegan);
    }
}
