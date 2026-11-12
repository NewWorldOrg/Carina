using Carina.Domain.Encodings;

namespace Carina.Domain.Playback;

/// <summary>
/// Where what was taken from a recording's file sits on the source a playback plan plays. It is placed at a zero,
/// or left unplaced: the plan hands over no artefact whose job says where its file's clock began, or the artefact was
/// made from a file whose clock began elsewhere, which <see cref="Artefact"/> and <see cref="ArtefactBegan"/> name.
/// </summary>
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
