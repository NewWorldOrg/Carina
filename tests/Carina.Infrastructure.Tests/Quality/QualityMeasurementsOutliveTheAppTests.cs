using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.DependencyInjection;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Quality;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.Quality;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class QualityMeasurementsOutliveTheAppTests(RepositoryDatabase database)
{
    private const int Service = 1_024;

    private static readonly DateTime Noon = new(2031, 3, 3, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan ALittleLater = TimeSpan.FromSeconds(10);

    private static readonly TuningParameters Tuned = TuningParameters.Terrestrial(27);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "what one app measured and the anomalies it opened are read back by the app that replaced it, which opens none of them a second time")]
    public async Task WhatOneAppMeasuredIsReadBackByTheAppThatReplacedIt()
    {
        int network = BroadcastIds.NextNetwork();
        string tuner = FormattableString.Invariant($"adapter{network}.frontend0");
        QualitySubject channel = QualitySubject.Of(
            QualitySubjectKind.Channel,
            FormattableString.Invariant($"{network}-{Service}"));
        QualitySubject device = QualitySubject.Of(QualitySubjectKind.Tuner, tuner);
        HandTurnedClock clock = new(Noon);
        SamplingDriverStandIn driver = Holding(tuner, carrierToNoise: 9_000, clock);
        Recording recorded = Recorded(network, tuner);

        IReadOnlyList<Guid> opened;

        await using (ServiceProvider first = App(network, driver, clock))
        {
            await InAScopeAsync(first, async services =>
            {
                IRecordingRepository recordings = services.GetRequiredService<IRecordingRepository>();

                await recordings.AddAsync(recorded, Cancel);

                recorded.Measure(
                    DropCounters.Counted(5_000, 1_000_000),
                    DropTimeline.Unlocated,
                    0,
                    0,
                    Noon.AddMinutes(-1));

                await recordings.SaveAsync(recorded, Cancel);
            });
            await InAScopeAsync(first, services => services.GetRequiredService<SignalSampleRound>().TakeAsync(Cancel));

            clock.Turn(ALittleLater);

            await InAScopeAsync(first, services => services.GetRequiredService<SupplyWatchRound>().WatchAsync(Cancel));

            opened = [.. (await BreachesAsync(first, channel, device)).Select(incident => incident.Id.Value).Order()];

            Assert.Equal(2, opened.Count);
        }

        clock.Turn(TimeSpan.FromMinutes(1));
        driver.Tuners = Holding(tuner, carrierToNoise: 9_000, clock).Tuners;

        await using ServiceProvider second = App(network, driver, clock);

        await InAScopeAsync(second, async services =>
        {
            QualityPeriod lastDay = QualityPeriod.Of(Noon.AddDays(-1), clock.GetUtcNow().UtcDateTime, clock.GetUtcNow().UtcDateTime)!;

            QualityLedgerRow row = Assert.Single(
                await services.GetRequiredService<IQualityLedgerReader>().ReadAsync(lastDay, Cancel),
                row => row.Recording.Equals(recorded.Id));

            Assert.True(row.Counters.Measured);
            Assert.Equal(5_000, row.Counters.Dropped);
            Assert.Equal(1_000_000, row.Counters.Total);

            SignalFigures figures = Assert.Single(
                await services.GetRequiredService<IQualitySignalReader>().FiguresAsync(lastDay, Cancel),
                figures => figures.Tuner.Value == tuner);

            Assert.Equal(1, figures.Samples);
            Assert.Equal(9_000, figures.CarrierToNoiseLowest);
            Assert.Equal(9_000, figures.CarrierToNoiseUsual);
        });

        IReadOnlyList<QualityIncident> standing = await BreachesAsync(second, channel, device);

        Assert.Equal(opened, standing.Select(incident => incident.Id.Value).Order());
        Assert.Equal(
            [QualityThresholdKey.PacketsLostUnwatchable, QualityThresholdKey.CarrierToNoiseFloor],
            standing.Select(incident => incident.Breached).Order());
        Assert.All(standing, incident => Assert.Equal(QualityIncidentState.Notified, incident.State));
        Assert.All(standing, incident => Assert.Equal(Noon + ALittleLater, incident.DetectedAt));

        await InAScopeAsync(second, services => services.GetRequiredService<SignalSampleRound>().TakeAsync(Cancel));

        clock.Turn(ALittleLater);

        await InAScopeAsync(second, services => services.GetRequiredService<SupplyWatchRound>().WatchAsync(Cancel));

        Assert.Equal(
            opened,
            (await BreachesAsync(second, channel, device)).Select(incident => incident.Id.Value).Order());
    }

    private static async Task InAScopeAsync(ServiceProvider app, Func<IServiceProvider, Task> working)
    {
        await using AsyncServiceScope scope = app.CreateAsyncScope();

        await working(scope.ServiceProvider);
    }

    private static async Task<IReadOnlyList<QualityIncident>> BreachesAsync(
        ServiceProvider app,
        QualitySubject channel,
        QualitySubject device)
    {
        await using AsyncServiceScope scope = app.CreateAsyncScope();

        return
        [
            .. (await scope.ServiceProvider.GetRequiredService<IQualityIncidentRepository>().ListUnsettledAsync(Cancel))
                .Where(incident => incident.Breached is not QualityThresholdKey.SupplySilence
                                   && (incident.Subject.Equals(channel) || incident.Subject.Equals(device))),
        ];
    }

    private ServiceProvider App(int network, IDriverClient driver, TimeProvider clock)
    {
        string connection;

        using (CarinaDbContext context = database.Open())
        {
            connection = context.Database.GetConnectionString()!;
        }

        return new ServiceCollection()
            .AddLogging()
            .AddCarinaInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Carina"] = connection,
                    ["CARINA_DRIVER_SOCKET"] = "/run/carina/driver.sock",
                    ["CARINA_DATA_PROTECTION_KEYS"] = "/var/lib/carina/keys",
                })
                .Build())
            .AddSingleton(driver)
            .AddSingleton(clock)
            .AddSingleton<IBroadcastStreamDirectory>(new HeldStreams(
            [
                new BroadcastStream(
                    new NetworkId(network),
                    new TransportStreamId(network),
                    Tuned,
                    [new ServiceId(Service)]),
            ]))
            .BuildServiceProvider();
    }

    private static SamplingDriverStandIn Holding(string tuner, int carrierToNoise, TimeProvider clock)
        => new()
        {
            Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Reached(
            [
                new TunerSnapshot(tuner, TunerKind.Terrestrial, TunerState.Busy)
                {
                    CurrentSession = new CurrentSessionDto
                    {
                        SessionId = SessionId.Parse("live-1"),
                        Purpose = SessionPurpose.Live,
                        Tune = Tuned.Typed(),
                        StartedAt = clock.GetUtcNow().AddMinutes(-1),
                    },
                    SignalQuality = new SignalQualityDto
                    {
                        Lock = SignalLock.Locked,
                        CnrMilliDecibels = carrierToNoise,
                        MeasuredAt = clock.GetUtcNow(),
                        LockReadAt = clock.GetUtcNow(),
                    },
                },
            ]),
        };

    private static Recording Recorded(int network, string tuner)
    {
        RecordingId id = RecordingId.New();
        DateTime airs = Noon.AddHours(-2);

        return Recording.Begin(
            id,
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(network), new ServiceId(Service), new EventId(1), airs),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            airs,
            airs.AddHours(3),
            new ProgrammeSnapshot(
                "A programme",
                string.Empty,
                string.Empty,
                [],
                airs.AddHours(-6),
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            airs,
            new TunerDeviceId(tuner));
    }
}
