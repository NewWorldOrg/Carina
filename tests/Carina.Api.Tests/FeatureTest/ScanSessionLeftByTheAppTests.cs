using System.Runtime.Versioning;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

[SupportedOSPlatform("linux")]
public sealed class ScanSessionLeftByTheAppTests
{
    [Fact]
    public async Task AScanSessionTheAppWalkedAwayFromEndsItselfAtTheDriversWalkingLimit()
    {
        MovedClock driverClock = new();
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync(
            reshapeDriver: services => services.AddSingleton<TimeProvider>(driverClock));

        TimeSpan walkingLimit = TimeSpan.FromMinutes(feature.Driver.Configuration.WalkSessionMinutes);
        SessionId scan = await StartScanSessionAsync(feature, endsAt: null);

        await feature.StopAppAsync();

        Assert.Equal(SessionState.Active, feature.Driver.Held(scan).State);

        driverClock.Move(walkingLimit - TimeSpan.FromMinutes(1));

        Assert.Equal(SessionState.Active, feature.Driver.Held(scan).State);

        driverClock.Move(TimeSpan.FromMinutes(2));

        await Eventually.Happens(
            () => feature.Driver.Held(scan).StopReason is SessionStopReason.EndTimeReached,
            "the driver ends the scan session the app left behind once the walking limit passes");
    }

    [Fact]
    public async Task AScanSessionAskingToOutlastTheWalkingLimitIsStillEndedAtIt()
    {
        MovedClock driverClock = new();
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync(
            reshapeDriver: services => services.AddSingleton<TimeProvider>(driverClock));

        TimeSpan walkingLimit = TimeSpan.FromMinutes(feature.Driver.Configuration.WalkSessionMinutes);
        SessionId scan = await StartScanSessionAsync(feature, endsAt: driverClock.GetUtcNow().AddHours(4));

        await feature.StopAppAsync();

        driverClock.Move(walkingLimit + TimeSpan.FromMinutes(1));

        await Eventually.Happens(
            () => feature.Driver.Held(scan).StopReason is SessionStopReason.EndTimeReached,
            "the driver ends a scan session at its own limit rather than at the end the app asked for");
    }

    private static async Task<SessionId> StartScanSessionAsync(AppSwapFeature feature, DateTimeOffset? endsAt)
    {
        SessionId sessionId = SessionId.Parse($"scan-{Guid.NewGuid():n}");
        TuneParams tune = TuningParameters.Terrestrial(27).Typed();
        DriverCall<SessionSnapshot> started = await feature.App.Services
            .GetRequiredService<IDriverClient>()
            .StartSessionAsync(
                new StartSessionRequest
                {
                    SessionId = sessionId,
                    Purpose = SessionPurpose.Scan,
                    Tuning = tune.ToLegacyRequest(),
                    Tune = tune,
                    EndsAt = endsAt,
                },
                CancellationToken.None);

        Assert.True(
            started.TryGetValue(out SessionSnapshot? session),
            $"The driver did not start the scan session: {started.Outcome} {started.Failure} {started.Problem?.Title}.");
        Assert.Equal(SessionState.Active, session!.State);

        return sessionId;
    }

    private sealed class MovedClock : TimeProvider
    {
        private long aheadTicks;

        public void Move(TimeSpan by) => Interlocked.Add(ref aheadTicks, by.Ticks);

        public override DateTimeOffset GetUtcNow() => base.GetUtcNow().AddTicks(Interlocked.Read(ref aheadTicks));
    }
}
