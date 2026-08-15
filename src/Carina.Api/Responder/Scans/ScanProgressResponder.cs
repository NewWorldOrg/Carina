using Carina.Api.Services;
using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Scans;

namespace Carina.Api.Responder.Scans;

public sealed record ScanTargetResponder(
    TuneSystem System,
    int PhysicalChannel,
    int? TransportStreamId)
{
    public static ScanTargetResponder Of(TuningParameters tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        return new ScanTargetResponder(
            tuning.System,
            tuning.PhysicalChannel,
            tuning.TransportStreamId?.Value);
    }

    public static ScanTargetResponder? Of(TuneParams? tune)
        => tune is null
            ? null
            : new ScanTargetResponder(
                tune.System,
                tune.ToLegacyRequest().PhysicalChannel,
                tune.IsdbSBs?.Tsid);
}

public sealed record ScanMeasurementResponder(
    DateTimeOffset MeasuredAt,
    bool Locked,
    int? CnrMilliDecibels,
    long? PostViterbiErrorBits,
    long? PostViterbiTotalBits)
{
    public static ScanMeasurementResponder? Of(SignalMeasurement? measurement)
        => measurement is null
            ? null
            : new ScanMeasurementResponder(
                measurement.MeasuredAt,
                measurement.Locked,
                measurement.CnrMilliDecibels,
                measurement.PostViterbiErrorBits,
                measurement.PostViterbiTotalBits);
}

public sealed record ScanAttemptResponder(
    ScanTargetResponder Target,
    ScanAttemptOutcome Outcome,
    string? Detail,
    int? ObservedTransportStreamId,
    ScanMeasurementResponder? Measurement,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt)
{
    public static ScanAttemptResponder Of(ScanRunAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return new ScanAttemptResponder(
            ScanTargetResponder.Of(attempt.Tuning),
            attempt.Outcome,
            attempt.Detail,
            attempt.ObservedTransportStreamId?.Value,
            ScanMeasurementResponder.Of(attempt.Measurement),
            attempt.StartedAt,
            attempt.FinishedAt);
    }
}

public sealed record ScanChannelChangeResponder(
    ScanChangeKind Kind,
    ScanTargetResponder Target,
    int? TransportStreamId,
    ScanMeasurementResponder? Measurement)
{
    public static ScanChannelChangeResponder Of(ScanChannelChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new ScanChannelChangeResponder(
            change.Kind,
            ScanTargetResponder.Of(change.Tuning),
            change.TransportStreamId?.Value,
            ScanMeasurementResponder.Of(change.Measurement));
    }
}

public sealed record ScanServiceChangeResponder(
    ScanChangeKind Kind,
    int NetworkId,
    int ServiceId,
    string Name,
    ServiceCategory Category,
    IReadOnlyList<ScanChannelChangeResponder> Channels)
{
    public static ScanServiceChangeResponder Of(ScanServiceChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new ScanServiceChangeResponder(
            change.Kind,
            change.NetworkId.Value,
            change.ServiceId.Value,
            change.Name,
            change.Category,
            [.. change.Channels.Select(ScanChannelChangeResponder.Of)]);
    }
}

public sealed record RotationDepartureResponder(
    int NetworkId,
    int ServiceId,
    ScanTargetResponder Target,
    int ConsecutiveFailures,
    DateTimeOffset Since)
{
    public static RotationDepartureResponder Of(RotationDeparture departure)
    {
        ArgumentNullException.ThrowIfNull(departure);

        return new RotationDepartureResponder(
            departure.NetworkId.Value,
            departure.ServiceId.Value,
            ScanTargetResponder.Of(departure.Tuning),
            departure.ConsecutiveFailures,
            departure.Since);
    }
}

public sealed record ScanDifferenceResponder(
    IReadOnlyList<ScanServiceChangeResponder> Added,
    IReadOnlyList<ScanServiceChangeResponder> Updated,
    IReadOnlyList<ScanServiceChangeResponder> Missing,
    IReadOnlyList<RotationDepartureResponder> LeftRotation)
{
    public static ScanDifferenceResponder? Of(ScanDifference? difference)
        => difference is null
            ? null
            : new ScanDifferenceResponder(
                [.. difference.Added.Select(ScanServiceChangeResponder.Of)],
                [.. difference.Updated.Select(ScanServiceChangeResponder.Of)],
                [.. difference.Missing.Select(ScanServiceChangeResponder.Of)],
                [.. difference.Departures.Select(RotationDepartureResponder.Of)]);
}

public sealed record ScanRunResponder(
    Guid ScanId,
    ScanRunState State,
    string? DriverInstanceId,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? Reason)
{
    public static ScanRunResponder Of(ScanRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new ScanRunResponder(
            run.Id.Value,
            run.State,
            run.DriverInstanceId,
            run.StartedAt,
            run.FinishedAt,
            run.Reason);
    }
}

public sealed record ScanProgressResponder(
    ScanRunResponder Run,
    int Attempted,
    int Succeeded,
    int Failed,
    IReadOnlyList<ScanAttemptResponder> Attempts,
    ScanDifferenceResponder? Difference)
{
    public static ScanProgressResponder Of(ScanProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var failed = progress.Attempts.Count(attempt => attempt.Failed);

        return new ScanProgressResponder(
            ScanRunResponder.Of(progress.Run),
            progress.Attempts.Count,
            progress.Attempts.Count - failed,
            failed,
            [.. progress.Attempts.Select(ScanAttemptResponder.Of)],
            ScanDifferenceResponder.Of(progress.Difference));
    }
}
