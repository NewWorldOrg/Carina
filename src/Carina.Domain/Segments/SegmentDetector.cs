namespace Carina.Domain.Segments;

public sealed record RecordingPieces(IReadOnlyList<SegmentPiece> Pieces, IReadOnlyList<TimeSpan> Snaps, TimeSpan Length, string Station, string? Series)
{
    public IReadOnlyList<JoinedPiece> Joined { get; init; } = [];

    public IReadOnlyList<BoundaryCandidate> Candidates { get; init; } = [];
}

public static class SegmentDetector
{
    public static SegmentDetection Detect(SegmentEvidence evidence, SegmentContext context, SegmentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return Detect(Read(evidence, context, model.Thresholds), model);
    }

    public static RecordingPieces Read(SegmentEvidence evidence, SegmentContext context, SegmentThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(thresholds);

        bool[]? sightings = context.Logo.Telling(evidence.Outlines);
        IReadOnlyList<BoundaryCandidate> candidates = BoundaryCandidates.Find(evidence, context, sightings, thresholds);
        (IReadOnlyList<SegmentPiece> pieces, IReadOnlyList<JoinedPiece> joined) = SegmentFeatures.Read(candidates, evidence, context, sightings, thresholds);

        return new RecordingPieces(pieces, BoundaryCandidates.Snaps(candidates), evidence.Length, context.Station, context.Series)
        {
            Joined = joined,
            Candidates = candidates,
        };
    }

    public static SegmentDetection Detect(RecordingPieces read, SegmentModel model)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(model);

        SegmentChances[] chances = [.. read.Pieces.Select(piece => model.Chances(piece.Features, read.Station, read.Series))];
        SegmentChances[] joined = [.. read.Joined.Select(piece => model.Chances(piece.Piece.Features, read.Station, read.Series))];

        return SegmentDetection.Judged(
            SegmentSequence.Decided(read.Pieces, chances, read.Joined, joined, read.Length, model.Thresholds),
            read.Length,
            read.Snaps);
    }
}
