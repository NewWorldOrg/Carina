using Carina.Domain.Base;
using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public sealed record ServiceCoverage(ServiceId ServiceId, DateTime? CoveredUntil, bool WasEverCollected);

public sealed record StreamCoverage(
    NetworkId NetworkId,
    TransportStreamId TransportStreamId,
    IReadOnlyList<ServiceCoverage> Services,
    DateTime? LastCompletedAt,
    DateTime? NotBefore);

public sealed record PlannedVisit(NetworkId NetworkId, TransportStreamId TransportStreamId, VisitReason Reason);

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
        TimeSpan wanted,
        bool hurried = false)
    {
        ArgumentNullException.ThrowIfNull(streams);
        UtcTimes.Required(now, nameof(now));

        var due = new List<(StreamCoverage Stream, VisitReason Reason, DateTime? Thinnest)>();

        foreach (StreamCoverage stream in streams)
        {
            DateTime? notBefore = UtcTimes.Optional(stream.NotBefore, nameof(streams));

            if (!hurried && notBefore is { } waited && waited > now)
            {
                continue;
            }

            DateTime? lastCompletedAt = UtcTimes.Optional(stream.LastCompletedAt, nameof(streams));

            if (lastCompletedAt is null || AwaitsAFirstCollection(stream))
            {
                due.Add((stream, VisitReason.NeverCollected, null));

                continue;
            }

            DateTime? thinnest = ThinnestOf(stream);

            due.Add((
                stream,
                thinnest is not null && thinnest < now + wanted ? VisitReason.ThinnestCoverage : VisitReason.Rotation,
                thinnest));
        }

        return
        [
            .. due
                .OrderBy(entry => entry.Reason)
                .ThenBy(entry => entry.Reason == VisitReason.Rotation
                    ? entry.Stream.LastCompletedAt ?? DateTime.MinValue
                    : entry.Thinnest ?? DateTime.MaxValue)
                .ThenBy(entry => entry.Stream.NetworkId.Value)
                .ThenBy(entry => entry.Stream.TransportStreamId.Value)
                .Select(entry => new PlannedVisit(
                    entry.Stream.NetworkId,
                    entry.Stream.TransportStreamId,
                    entry.Reason)),
        ];
    }

    public static bool AwaitsAFirstCollection(StreamCoverage stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        foreach (ServiceCoverage service in stream.Services)
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

        foreach (ServiceCoverage service in stream.Services)
        {
            if (!service.WasEverCollected
                || UtcTimes.Optional(service.CoveredUntil, nameof(stream)) is not { } until)
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
