using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.TestSupport;

using Puppis.Contracts;

namespace Carina.Domain.Tests.Unit.Quality;

[Trait("Category", "Unit")]
public sealed class SupplyWatchDomainServiceTests
{
    private const string Device = "adapter3.frontend0";

    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly HeldQualitySignalSamples samples = new();

    private SupplyWatchDomainService Watch => new(samples);

    [Fact(DisplayName = "BR-QD-007: a tuner in use holding a session is watched for samples from when its session opened")]
    public async Task ATunerHoldingASessionIsWatchedFromWhenItOpened()
    {
        SupplyReading reading = Assert.Single(await ReadAsync(Holding(Noon.AddMinutes(-10))));

        Assert.Equal(SupplySilence.SignalSamples, reading.Silence);
        Assert.Equal(QualitySubject.Of(QualitySubjectKind.Tuner, Device), reading.Subject);
        Assert.Equal(Noon.AddMinutes(-10), reading.LastHeardAt);
        Assert.Null(reading.Allowed);
    }

    [Fact(DisplayName = "BR-QD-007: a disabled tuner, one holding nothing, a session nobody named or one with no opening time is not watched")]
    public async Task OnlyATunerInUseHoldingANamedSessionIsWatched()
        => Assert.Empty(await ReadAsync(
            Holding(Noon, state: TunerState.Disabled),
            new TunerSnapshot(Device, TunerKind.Terrestrial, TunerState.Idle),
            Holding(Noon, session: null),
            Holding(null),
            Holding(Noon, device: " ")));

    [Fact(DisplayName = "BR-QD-007: a tuner is last heard at its latest sample when that is later than its session opened")]
    public async Task ALaterSampleIsWhenTheTunerWasLastHeard()
    {
        samples.Samples.Add(Sample(Noon.AddMinutes(-2)));

        Assert.Equal(Noon.AddMinutes(-2), Assert.Single(await ReadAsync(Holding(Noon.AddMinutes(-10)))).LastHeardAt);
    }

    [Fact(DisplayName = "BR-QD-007: a sample from before the session opened, or on the instant it opened, leaves the opening as when it was last heard")]
    public async Task ASampleNoLaterThanTheOpeningLeavesTheOpening()
    {
        samples.Samples.Add(Sample(Noon.AddMinutes(-20)));

        Assert.Equal(Noon.AddMinutes(-10), Assert.Single(await ReadAsync(Holding(Noon.AddMinutes(-10)))).LastHeardAt);

        samples.Samples.Add(Sample(Noon.AddMinutes(-10)));

        Assert.Equal(Noon.AddMinutes(-10), Assert.Single(await ReadAsync(Holding(Noon.AddMinutes(-10)))).LastHeardAt);
    }

    [Fact(DisplayName = "BR-QD-007: a session the driver says opened later than now counts from now")]
    public async Task ASessionOpenedLaterThanNowCountsFromNow()
        => Assert.Equal(Noon, Assert.Single(await ReadAsync(Holding(Noon.AddMinutes(1)))).LastHeardAt);

    private Task<IReadOnlyList<SupplyReading>> ReadAsync(params TunerSnapshot[] tuners)
        => Watch.SignalSamplesAsync(tuners, Noon, Cancel);

    private static TunerSnapshot Holding(
        DateTime? openedAt,
        TunerState state = TunerState.Busy,
        string? session = "live-1",
        string device = Device)
        => new(device, TunerKind.Terrestrial, state)
        {
            CurrentSession = new CurrentSessionDto
            {
                SessionId = session is null ? default : SessionId.Parse(session),
                Purpose = SessionPurpose.Live,
                StartedAt = openedAt is { } opened ? new DateTimeOffset(opened) : null,
            },
        };

    private static QualitySignalSample Sample(DateTime takenAt)
        => SignalSampleIntake.Take(
            new SignalReadingAsk(
                "instance-a",
                SessionId.Parse("live-1"),
                SessionPurpose.Live,
                new TunerDeviceId(Device),
                new NetworkId(32736),
                new ServiceId(1024),
                null),
            takenAt);
}
