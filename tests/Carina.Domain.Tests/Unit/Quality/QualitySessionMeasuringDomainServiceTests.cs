using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.TestSupport;

using Puppis.Contracts;

namespace Carina.Domain.Tests.Unit.Quality;

[Trait("Category", "Unit")]
public sealed class QualitySessionMeasuringDomainServiceTests
{
    private const string Device = "adapter3.frontend0";

    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTimeOffset Started = new(2026, 9, 8, 11, 50, 0, TimeSpan.Zero);

    private static readonly TuningParameters Terrestrial = TuningParameters.Terrestrial(27);

    private static readonly TuningParameters Unnamed = TuningParameters.Terrestrial(52);

    private static readonly DriverHello Hello =
        new(DriverProtocol.Version, "instance-a", [DriverCapabilities.CcMeasurement]);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly HeldQualitySessionMeasurements measurements = new();

    private QualitySessionMeasuringDomainService Measuring => new(
        new HeldStreams(
        [
            new BroadcastStream(
                new NetworkId(32736),
                new TransportStreamId(32736),
                Terrestrial,
                [new ServiceId(1024), new ServiceId(1025)]),
        ]),
        measurements);

    [Fact(DisplayName = "BR-QV-003: a sample is taken from a tuner holding a named session, filed under the first service of its multiplex")]
    public async Task ASampleIsTakenFromATunerHoldingANamedSession()
    {
        SignalSampleSources sources = await Measuring.LocateAsync([Holding("live-1", Terrestrial)], Cancel);

        SignalSampleSource source = Assert.Single(sources.Sources);

        Assert.Equal(new SignalSite(new TunerDeviceId(Device), new NetworkId(32736), new ServiceId(1024)), source.Site);
        Assert.Equal(Terrestrial.PhysicalChannel, source.PhysicalChannel);
        Assert.Equal("instance-a", source.AskOf("instance-a").DriverInstanceId);
        Assert.Equal(0, sources.Unnamed);
    }

    [Fact(DisplayName = "BR-QD-004: a tuner holding nothing, a session nobody named or no tune is not tuned to be measured")]
    public async Task ATunerHoldingNoNamedTunedSessionGivesNoSample()
    {
        SignalSampleSources sources = await Measuring.LocateAsync(
        [
            new TunerSnapshot(Device, TunerKind.Terrestrial, TunerState.Idle),
            Holding(null, Terrestrial),
            Holding("live-2", null),
            Holding("live-3", Terrestrial, device: " "),
        ],
            Cancel);

        Assert.Empty(sources.Sources);
        Assert.Equal(0, sources.Unnamed);
    }

    [Fact(DisplayName = "BR-QV-003: a session held on a multiplex no candidate channel names is counted and not sampled")]
    public async Task ASessionOnAMultiplexNoCandidateNamesIsCountedOnly()
    {
        SignalSampleSources sources = await Measuring.LocateAsync([Holding("live-1", Unnamed)], Cancel);

        Assert.Empty(sources.Sources);
        Assert.Equal(1, sources.Unnamed);
        Assert.Null(sources.SiteOf(Device));
    }

    [Fact(DisplayName = "BR-QD-005: a session on a sampled tuner is opened in the session ledger and its counts kept")]
    public async Task ASessionOnASampledTunerIsOpenedAndCounted()
    {
        QualitySessionTally tally = await MeasureAsync([Session("live-1", SessionPurpose.Live)]);

        QualitySessionMeasurement kept = Assert.Single(measurements.Measurements);

        Assert.Equal(new QualitySessionTally(1, 0), tally);
        Assert.Equal(Started.UtcDateTime, kept.StartedAt);
        Assert.Equal(12, kept.CcDroppedPackets);
        Assert.Equal(9012, kept.CcTotalPackets);
        Assert.False(kept.HasEnded);
    }

    [Fact(DisplayName = "BR-QD-005: what a recording session measured is left to the recording ledger")]
    public async Task ARecordingSessionIsNotMeasuredHere()
    {
        QualitySessionTally tally = await MeasureAsync([Session("rec-1", SessionPurpose.Recording)]);

        Assert.Equal(new QualitySessionTally(0, 0), tally);
        Assert.Empty(measurements.Measurements);
    }

    [Fact(DisplayName = "BR-QD-005: a session that concluded before it was ever measured is not opened")]
    public async Task ASessionThatConcludedUnmeasuredIsNotOpened()
    {
        QualitySessionTally tally = await MeasureAsync([Session("live-1", SessionPurpose.Live, concluded: true)]);

        Assert.Equal(new QualitySessionTally(0, 0), tally);
        Assert.Empty(measurements.Measurements);
    }

    [Fact(DisplayName = "BR-QD-005: a session on a tuner this pass did not sample has nowhere to be filed and is not opened")]
    public async Task ASessionOnATunerNotSampledIsNotOpened()
    {
        QualitySessionTally tally = await MeasureAsync(
            [Session("live-1", SessionPurpose.Live, device: "adapter0.frontend0")]);

        Assert.Equal(new QualitySessionTally(0, 0), tally);
        Assert.Empty(measurements.Measurements);
    }

    [Fact(DisplayName = "BR-QD-005: a measured session the driver says has concluded is closed with its counts")]
    public async Task AConcludedSessionIsClosed()
    {
        await MeasureAsync([Session("live-1", SessionPurpose.Live)]);

        QualitySessionTally tally = await MeasureAsync([Session("live-1", SessionPurpose.Live, concluded: true)]);

        QualitySessionMeasurement kept = Assert.Single(measurements.Measurements);

        Assert.Equal(new QualitySessionTally(1, 1), tally);
        Assert.Equal(Noon, kept.EndedAt);
        Assert.Equal(9012, kept.CcTotalPackets);
    }

    [Fact(DisplayName = "BR-QD-005: a measurement whose session the driver no longer lists is closed")]
    public async Task AMeasurementTheDriverNoLongerListsIsClosed()
    {
        await MeasureAsync([Session("live-1", SessionPurpose.Live)]);

        QualitySessionTally tally = await MeasureAsync([]);

        Assert.Equal(new QualitySessionTally(0, 1), tally);
        Assert.Equal(Noon, Assert.Single(measurements.Measurements).EndedAt);
    }

    [Fact(DisplayName = "BR-QD-005: a measurement another driver opened is closed when this driver lists a session of the same name")]
    public async Task AMeasurementAnotherDriverOpenedIsClosed()
    {
        await measurements.SaveAsync(
            QualitySessionMeasurement.Open(
                "instance-b",
                SessionId.Parse("live-1"),
                SessionPurpose.Live,
                new TunerDeviceId(Device),
                new NetworkId(32736),
                new ServiceId(1024),
                Started.UtcDateTime),
            Cancel);

        await MeasureAsync([Session("live-1", SessionPurpose.Live)]);

        Assert.Contains(measurements.Measurements, kept => kept.DriverInstanceId == "instance-b" && kept.HasEnded);
        Assert.Contains(measurements.Measurements, kept => kept.DriverInstanceId == "instance-a" && !kept.HasEnded);
    }

    [Fact(DisplayName = "BR-QD-005: a session closed at a time that reads earlier than its start ends at its start")]
    public async Task ASessionClosedBeforeItsStartEndsAtItsStart()
    {
        await MeasureAsync([Session("live-1", SessionPurpose.Live)]);

        await MeasureAsync([], at: Started.UtcDateTime.AddMinutes(-1));

        Assert.Equal(Started.UtcDateTime, Assert.Single(measurements.Measurements).EndedAt);
    }

    private async Task<QualitySessionTally> MeasureAsync(IReadOnlyList<SessionSnapshot> sessions, DateTime? at = null)
    {
        QualitySessionMeasuringDomainService measuring = Measuring;
        SignalSampleSources sources = await measuring.LocateAsync([Holding("live-1", Terrestrial)], Cancel);

        return await measuring.MeasureAsync(Hello, sessions, sources, at ?? Noon, Cancel);
    }

    private static TunerSnapshot Holding(string? session, TuningParameters? tuning, string device = Device)
        => new(device, TunerKind.Terrestrial, TunerState.Busy)
        {
            CurrentSession = new CurrentSessionDto
            {
                SessionId = session is null ? default : SessionId.Parse(session),
                Purpose = SessionPurpose.Live,
                Tune = tuning?.Typed(),
            },
        };

    private static SessionSnapshot Session(
        string id,
        SessionPurpose purpose,
        bool concluded = false,
        string device = Device)
        => new(SessionId.Parse(id), purpose, device, concluded ? SessionState.Stopped : SessionState.Active, Started)
        {
            Counters = new SessionCounters(Packets: 9000, Drops: 12, DeviceOverflows: 3, CcMeasured: true),
            Concluded = concluded,
        };
}
