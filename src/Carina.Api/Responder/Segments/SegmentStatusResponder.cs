using Carina.Api.Services;
using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

/// <summary>
/// How much learning data is kept: the recordings it was taken from, including those thrown away since, the
/// whole seconds of them read, the bytes the segment tables take in the database, and the ended recordings
/// still waiting to be read.
/// </summary>
public sealed record LearningDataResponder(int Recordings, long Seconds, long Bytes, int Waiting)
{
    public static LearningDataResponder Of(LearningDataAmount amount)
    {
        ArgumentNullException.ThrowIfNull(amount);

        return new LearningDataResponder(
            amount.Recordings,
            (long)Math.Floor(amount.Duration.TotalSeconds),
            amount.Bytes,
            amount.Waiting);
    }
}

/// <summary>
/// How many entries the catalog of repeated sound holds of each kind.
/// </summary>
public sealed record CatalogResponder(int Cm, int Opening, int Ending, int ProgrammeFixture, int StationFixture, int Unknown)
{
    public static CatalogResponder Of(CatalogCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        return new CatalogResponder(counts.Cm, counts.Opening, counts.Ending, counts.ProgrammeFixture, counts.StationFixture, counts.Unknown);
    }
}

/// <summary>
/// The version of the model in use: its number, where it stands, whether it was chosen by hand, and when it was
/// made and put in use.
/// </summary>
public sealed record SegmentVersionResponder(int Number, SegmentModelVersionState State, bool ByHand, DateTime TrainedAt, DateTime? AdoptedAt)
{
    public static SegmentVersionResponder Of(SegmentModelVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new SegmentVersionResponder(version.Number, version.State, version.ByHand, version.TrainedAt, version.AdoptedAt);
    }
}

/// <summary>
/// Where CM, OP and ED detection stands.
/// </summary>
public sealed record SegmentStatusResponder(LearningDataResponder LearningData, CatalogResponder Catalog, SegmentVersionResponder Version)
{
    public static SegmentStatusResponder Of(SegmentStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new SegmentStatusResponder(
            LearningDataResponder.Of(status.LearningData),
            CatalogResponder.Of(status.Catalog),
            SegmentVersionResponder.Of(status.Version));
    }
}
