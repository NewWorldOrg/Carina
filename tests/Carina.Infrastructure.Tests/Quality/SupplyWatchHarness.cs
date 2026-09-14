using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Quality;
using Carina.Infrastructure.Quality;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Quality;

internal sealed class WatchedSupply : IQualitySupplyReader
{
    public List<SupplyReading> Readings { get; } = [];

    public Task<IReadOnlyList<SupplyReading>> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SupplyReading>>([.. Readings]);
}

internal sealed class SupplyWatchHarness
{
    public const string Device = "adapter3.frontend0";

    public static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    public HandTurnedClock Clock { get; } = new(Noon);

    public HeldQualityThresholds Thresholds { get; } = new();

    public HeldQualityIncidents Incidents { get; } = new();

    public WatchedSupply Supply { get; } = new();

    public HeldQualitySignalSamples Samples { get; } = new();

    public SamplingDriverStandIn Driver { get; } = new();

    public SupplyStandingBoard Board { get; } = new();

    public SilentEvents Events { get; } = new();

    public SupplyWatchRound Round()
        => new(
            Thresholds,
            Incidents,
            Supply,
            Samples,
            Driver,
            Board,
            Events,
            Clock,
            NullLogger<SupplyWatchRound>.Instance);

    public void HoldingATuner(DateTimeOffset openedAt)
        => Driver.Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Reached(
        [
            new TunerSnapshot(Device, TunerKind.Terrestrial, TunerState.Busy)
            {
                CurrentSession = new CurrentSessionDto
                {
                    SessionId = SessionId.Parse("live-1"),
                    Purpose = SessionPurpose.Live,
                    StartedAt = openedAt,
                },
            },
        ]);

    public void HoldingNothing()
        => Driver.Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Reached(
            [new TunerSnapshot(Device, TunerKind.Terrestrial, TunerState.Idle)]);

    public void Writing(DateTime startedAt, TimeSpan written, DateTime? measuredAt)
    {
        QualitySubject subject = QualitySubject.Of(QualitySubjectKind.Recording, Guid.NewGuid().ToString("N"));

        Supply.Readings.Add(SupplyReading.Of(SupplySilence.RecordingProgress, subject, startedAt + written));
        Supply.Readings.Add(SupplyReading.Of(
            SupplySilence.RecordingMeasurement,
            subject,
            measuredAt ?? startedAt));
    }

    public void Visited(DateTime at)
        => Supply.Readings.Add(SupplyReading.Of(
            SupplySilence.GuideVisits,
            QualitySubject.TheGuideLedger,
            at + TimeSpan.FromHours(6)));
}
