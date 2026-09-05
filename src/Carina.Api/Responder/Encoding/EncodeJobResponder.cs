using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Encodings;

namespace Carina.Api.Responder.Encoding;

public sealed record EncodeRouteResponder(EncodeEncoder Asked, EncodeEncoder Ran, EncodeSwerve? Swerved);

public sealed record EncodeHeadwayResponder(double? Portion, int? LeftSeconds, DateTime At);

public sealed record EncodeFailureResponder(EncodeFailure Failure, string Note, DateTime NoticedAt);

/// <summary>
/// One job as the ledger holds it, read at a moment: the standing is the ledger's five-valued word,
/// and beside it stands what the reader works out from the time — how long the job has gone without
/// making headway, and whether that is long enough to call it stalled (BR-ED2-014). The programme's
/// id stays in the ledger; it is not a thing a caller does anything with.
/// </summary>
public sealed record EncodeJobResponder(
    Guid Id,
    string RecordingId,
    Guid ProfileId,
    Guid DestinationId,
    string OutputRoot,
    EncodeJobStatus Status,
    int Attempt,
    DateTime QueuedAt,
    DateTime? StartedAt,
    DateTime? EndedAt,
    EncodeRouteResponder? Route,
    EncodeHeadwayResponder? Headway,
    int? QuietForSeconds,
    bool Stalled,
    EncodeFailureResponder? Failure,
    string? ArtefactName)
{
    public static EncodeJobResponder Of(EncodeJobView seen)
    {
        ArgumentNullException.ThrowIfNull(seen);

        EncodeJob job = seen.Job;

        return new EncodeJobResponder(
            job.Id.Value,
            job.RecordingId.Wire,
            job.ProfileId.Value,
            job.DestinationId.Value,
            job.OutputRoot.Value,
            job.Status,
            job.Attempt,
            job.QueuedAt,
            job.StartedAt,
            job.EndedAt,
            job.Route is { } route ? new EncodeRouteResponder(route.Asked, route.Ran, route.Swerved) : null,
            job.Headway is { } headway ? new EncodeHeadwayResponder(headway.Portion, WholeSeconds(headway.Left), headway.At) : null,
            WholeSeconds(seen.QuietFor),
            seen.Stalled,
            job.Failure is { } failure ? new EncodeFailureResponder(failure.Failure, failure.Note, failure.NoticedAt) : null,
            job.ArtefactName?.Value);
    }

    private static int? WholeSeconds(TimeSpan? span)
        => span is { } some ? (int)Math.Min(int.MaxValue, Math.Floor(some.TotalSeconds)) : null;
}

public sealed record EncodeJobListResponder(
    IReadOnlyList<EncodeJobResponder> Items,
    int Total,
    int CurrentPage,
    int LastPage,
    int PerPage)
{
    public static EncodeJobListResponder Of(PaginatedList<EncodeJobView> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        return new EncodeJobListResponder(
            [.. found.Items.Select(EncodeJobResponder.Of)],
            found.Total,
            found.CurrentPage,
            found.LastPage,
            found.PerPage);
    }
}
