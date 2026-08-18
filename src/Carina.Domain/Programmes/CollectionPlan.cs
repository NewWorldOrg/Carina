namespace Carina.Domain.Programmes;

public sealed record ServiceCoverage(int ServiceId, DateTime? CoveredUntil, bool WasEverCollected);

public sealed record StreamCoverage(
    int NetworkId,
    int TransportStreamId,
    IReadOnlyList<ServiceCoverage> Services,
    DateTime? LastCompletedAt,
    DateTime? NotBefore);

public sealed record PlannedVisit(int NetworkId, int TransportStreamId, VisitReason Reason);

public enum VisitReason
{
    NeverCollected = 0,

    ThinnestCoverage = 1,

    Rotation = 2,
}

public static class CollectionPlan
{
    public static IReadOnlyList<PlannedVisit> Of(
        IReadOnlyList<StreamCoverage> streams,
        DateTime now,
        TimeSpan wanted)
    {
        ArgumentNullException.ThrowIfNull(streams);

        var due = new List<(StreamCoverage Stream, VisitReason Reason, DateTime? Thinnest)>();

        foreach (var stream in streams)
        {
            if (stream.NotBefore is { } notBefore && notBefore > now)
            {
                continue;
            }

            if (stream.LastCompletedAt is null || AwaitsAFirstCollection(stream))
            {
                due.Add((stream, VisitReason.NeverCollected, null));

                continue;
            }

            var thinnest = ThinnestOf(stream);

            due.Add((
                stream,
                thinnest is not null && thinnest < now + wanted ? VisitReason.ThinnestCoverage : VisitReason.Rotation,
                thinnest));
        }

        return
        [
            .. due
                .OrderBy(entry => entry.Reason)
                .ThenBy(entry => entry.Thinnest ?? DateTime.MaxValue)
                .ThenBy(entry => entry.Stream.LastCompletedAt ?? DateTime.MinValue)
                .ThenBy(entry => entry.Stream.NetworkId)
                .ThenBy(entry => entry.Stream.TransportStreamId)
                .Select(entry => new PlannedVisit(
                    entry.Stream.NetworkId,
                    entry.Stream.TransportStreamId,
                    entry.Reason)),
        ];
    }

    public static bool AwaitsAFirstCollection(StreamCoverage stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        foreach (var service in stream.Services)
        {
            if (!service.WasEverCollected)
            {
                return true;
            }
        }

        return false;
    }

    public static DateTime? ThinnestOf(StreamCoverage stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        DateTime? thinnest = null;

        foreach (var service in stream.Services)
        {
            if (!service.WasEverCollected || service.CoveredUntil is not { } until)
            {
                continue;
            }

            if (thinnest is null || until < thinnest)
            {
                thinnest = until;
            }
        }

        return thinnest;
    }
}
