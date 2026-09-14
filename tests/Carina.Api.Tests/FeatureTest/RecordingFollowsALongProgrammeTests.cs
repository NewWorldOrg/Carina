using System.Runtime.Versioning;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
[SupportedOSPlatform("linux")]
public sealed class RecordingFollowsALongProgrammeTests
{
    [Fact]
    public async Task ARecordingAndItsTunerBothFollowAProgrammeThatRunsLate()
    {
        await using AppSwapFeature feature = await AppSwapFeature.StartAsync();

        RecordingId started = Assert.Single((await feature.App.TickAsync()).Started);
        Recording running = Assert.Single(feature.Recordings.Rows);
        DateTime promised = running.ExpectedWindowEnd;
        DateTime runsLate = promised.AddMinutes(5);

        Assert.Equal(promised, Assert.Single(await feature.App.SessionsAsync()).EndsAt?.UtcDateTime);

        Announce(feature, running, runsLate);

        RecordingRun followed = await feature.App.TickAsync();

        Assert.Equal(started, Assert.Single(followed.Followed).Id);
        Assert.False(Assert.Single(followed.Followed).EndUndecided);
        Assert.Equal(runsLate, Assert.Single(feature.Recordings.Rows).ExpectedWindowEnd);
        Assert.Equal(runsLate, Assert.Single(await feature.App.SessionsAsync()).EndsAt?.UtcDateTime);

        feature.Clock.Turn(AppSwapFeature.Window + TimeSpan.FromMinutes(1));

        RecordingRun pastTheOldWindow = await feature.App.TickAsync();

        Assert.Empty(pastTheOldWindow.Stopped);
        Assert.Empty(pastTheOldWindow.Followed);
        Assert.True(Assert.Single(feature.Recordings.Rows).IsInFlight);

        feature.Clock.Turn(TimeSpan.FromMinutes(5));

        Assert.Equal(started, Assert.Single((await feature.App.TickAsync()).Stopped));

        await feature.UntilTheRecordingSessionIsStopped();
    }

    private static void Announce(AppSwapFeature feature, Recording running, DateTime? endsAt)
        => feature.Programmes.Programmes.Add(Programme.Discover(
            new ProgrammeBroadcast(
                running.Programme.Id,
                new TransportStreamId(32736),
                running.ProgrammeStartsAt,
                endsAt,
                "A programme that runs late",
                "What it is about",
                false)
            {
                Source = ProgrammeSource.PresentFollowing,
            },
            running.ProgrammeStartsAt));
}
