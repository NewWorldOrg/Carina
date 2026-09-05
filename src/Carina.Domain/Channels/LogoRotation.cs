using Carina.Contracts;

namespace Carina.Domain.Channels;

public static class LogoRotation
{
    public static BroadcastStream? NextDue(
        IReadOnlyList<BroadcastStream> streams,
        IReadOnlyList<LogoVisit> visits,
        LogoSweepSettings settings,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(visits);
        ArgumentNullException.ThrowIfNull(settings);

        return streams
            .Where(CarriesACommonDataTable)
            .Select(stream => new { Stream = stream, Visit = VisitOf(visits, stream) })
            .Where(walked => walked.Visit is null || walked.Visit.DueAt(settings) <= now)
            .OrderBy(walked => walked.Visit?.LastAttemptedAt ?? DateTime.MinValue)
            .ThenBy(walked => walked.Stream.NetworkId.Value)
            .ThenBy(walked => walked.Stream.TransportStreamId.Value)
            .Select(walked => walked.Stream)
            .FirstOrDefault();
    }

    public static bool CarriesACommonDataTable(BroadcastStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return stream.Tuning.System is TuneSystem.IsdbT;
    }

    private static LogoVisit? VisitOf(IReadOnlyList<LogoVisit> visits, BroadcastStream stream)
        => visits.FirstOrDefault(visit =>
            visit.NetworkId.Equals(stream.NetworkId)
            && visit.TransportStreamId.Equals(stream.TransportStreamId));
}
