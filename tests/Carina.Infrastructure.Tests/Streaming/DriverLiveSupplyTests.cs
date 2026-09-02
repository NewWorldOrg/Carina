using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class DriverLiveSupplyTests
{
    private static readonly NetworkId Network = new(32736);

    private static readonly ServiceId Service = new(1024);

    private static readonly TuningParameters Channel27 = TuningParameters.Terrestrial(27);

    private readonly LiveDriverStandIn driver = new();

    private DriverObservation observed = DriverObservation.NotConnected;

    [Fact]
    public async Task OpeningStartsOneLiveSessionOnTheResolvedChannelAndTakesTheViewersSeatOnItOnce()
    {
        LiveSupplyStart opened = await Supply().OpenAsync(Network, Service, CancellationToken.None);

        Assert.True(opened.Flowing, opened.Note);

        StartSessionRequest asked = Assert.Single(driver.Started);

        Assert.Equal(SessionPurpose.Live, asked.Purpose);
        Assert.Equal(Channel27.Typed(), asked.Tune);
        Assert.Equal(Channel27.Typed().ToLegacyRequest(), asked.Tuning);
        Assert.Null(asked.OutputRoot);
        Assert.Null(asked.RecordingId);
        Assert.Null(asked.EndsAt);
        Assert.Null(asked.DeviceId);
        Assert.StartsWith(LiveSessions.Prefix, asked.SessionId.Value, StringComparison.Ordinal);
        Assert.Equal([(asked.SessionId, DriverEndpoints.ViewerSubscriber)], driver.Opened);
        Assert.Empty(opened.Stream!.Bytes.ToString() is null ? [] : driver.Stopped);
    }

    [Fact]
    public async Task WhatTheDriverSendsComesThroughUnchangedHoweverItIsCutUp()
    {
        byte[] sent = [.. Enumerable.Range(0, 5_000).Select(at => (byte)(at * 31 % 251))];
        LiveSupplyStart opened = await Supply().OpenAsync(Network, Service, CancellationToken.None);

        foreach (int piece in new[] { 37, 1, 700, 188, 4_074 })
        {
            int written = sent.Length - Remaining(piece, sent.Length);

            await driver.Writer.WriteAsync(sent.AsMemory(written, piece));
        }

        await driver.Writer.CompleteAsync();

        using MemoryStream received = new();

        await opened.Stream!.Bytes.CopyToAsync(received);

        Assert.Equal(sent, received.ToArray());
    }

    [Theory]
    [InlineData(TuningRefusal.NoSuchService, LiveRefusal.NoSuchChannel)]
    [InlineData(TuningRefusal.NoSelectedChannel, LiveRefusal.NoSuchChannel)]
    [InlineData(TuningRefusal.NoTunerForSystem, LiveRefusal.NoTunerFree)]
    [InlineData(TuningRefusal.CapacityUnknown, LiveRefusal.DriverUnavailable)]
    [InlineData(TuningRefusal.LedgerUnreadable, LiveRefusal.DriverUnavailable)]
    public async Task AServiceThatCannotBeTunedIsRefusedBeforeTheDriverIsAsked(TuningRefusal why, LiveRefusal expected)
    {
        LiveSupplyStart refused = await Supply(TuningResolution.Refused(why)).OpenAsync(Network, Service, CancellationToken.None);

        Assert.False(refused.Flowing);
        Assert.Equal(expected, refused.Refusal);
        Assert.Contains(why.ToString(), refused.Note, StringComparison.Ordinal);
        Assert.Empty(driver.Started);
    }

    [Theory]
    [InlineData(SessionRefusalTitles.NoDeviceFree, LiveRefusal.NoTunerFree)]
    [InlineData(SessionRefusalTitles.DeviceBusy, LiveRefusal.NoTunerFree)]
    [InlineData(SessionRefusalTitles.NoLock, LiveRefusal.WouldNotTune)]
    [InlineData(SessionRefusalTitles.DeviceUnavailable, LiveRefusal.WouldNotTune)]
    [InlineData(SessionRefusalTitles.FaultedDevice, LiveRefusal.WouldNotTune)]
    [InlineData(SessionRefusalTitles.DisabledDevice, LiveRefusal.WouldNotTune)]
    [InlineData(SessionRefusalTitles.NoDeviceOfThatKind, LiveRefusal.WouldNotTune)]
    [InlineData(SessionRefusalTitles.WrongDeviceKind, LiveRefusal.WouldNotTune)]
    [InlineData(SessionRefusalTitles.Draining, LiveRefusal.DriverUnavailable)]
    [InlineData(SessionRefusalTitles.CapabilityMissing, LiveRefusal.DriverUnavailable)]
    [InlineData(SessionRefusalTitles.Rejected, LiveRefusal.DriverUnavailable)]
    [InlineData("http500", LiveRefusal.DriverUnavailable)]
    public async Task ADriverThatRefusesTheSessionIsReadIntoTheReasonATunerCanHave(string title, LiveRefusal expected)
    {
        driver.RefusingToStart = new DriverProblem(title, ["what the driver said."]);

        LiveSupplyStart refused = await Supply().OpenAsync(Network, Service, CancellationToken.None);

        Assert.Equal(expected, refused.Refusal);
        Assert.Contains(title, refused.Note, StringComparison.Ordinal);
        Assert.Contains("what the driver said.", refused.Note, StringComparison.Ordinal);
        Assert.Empty(driver.Opened);
    }

    [Fact]
    public async Task ADriverThatCannotBeReachedIsUnavailable()
    {
        driver.Unreachable = true;

        LiveSupplyStart refused = await Supply().OpenAsync(Network, Service, CancellationToken.None);

        Assert.Equal(LiveRefusal.DriverUnavailable, refused.Refusal);
        Assert.Contains("could not be reached", refused.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASessionTheDriverHandsBackAlreadyFailedWouldNotTune()
    {
        driver.StateOnStart = SessionState.Failed;
        driver.FailureCauseOnStart = "the frontend did not lock.";

        LiveSupplyStart refused = await Supply().OpenAsync(Network, Service, CancellationToken.None);

        Assert.Equal(LiveRefusal.WouldNotTune, refused.Refusal);
        Assert.Equal("the frontend did not lock.", refused.Note);
        Assert.Empty(driver.Opened);
    }

    [Fact]
    public async Task AStreamTheDriverWillNotOpenStopsTheSessionItWasOpenedForAndRefuses()
    {
        driver.RefusingToOpen = new DriverProblem("tooManySubscribers", ["every seat is taken."]);

        LiveSupplyStart refused = await Supply().OpenAsync(Network, Service, CancellationToken.None);

        Assert.Equal(LiveRefusal.DriverUnavailable, refused.Refusal);
        Assert.Equal([(driver.Held!.Value, DriverLiveSupply.NoStreamBecause)], driver.Stopped);
    }

    [Fact]
    public async Task AViewerGivingUpBetweenTheSessionAndItsStreamLeavesNoSessionBehind()
    {
        driver.BeforeOpening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenSource givingUp = new();

        Task<LiveSupplyStart> opening = Supply().OpenAsync(Network, Service, givingUp.Token);

        await Eventually.Happens(() => driver.Held is not null, "the session is started before the stream is asked for");
        await givingUp.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);

        Assert.Equal([(driver.Held!.Value, DriverLiveSupply.GivenUpBecause)], driver.Stopped);
    }

    [Fact]
    public async Task AStreamThatEndsBecauseARecordingTookTheTunerSaysSoInTheDriversWords()
    {
        driver.Recalled = _ => DriverCall<SessionSnapshot>.Reached(
            driver.Snapshot(SessionState.Failed, SessionStopReason.Preempted, "The tuner 'adapter3' goes to 'rec-1' for recording, which outranks 'live-1'."));

        LiveSupplyEnding ending = await EndedAsync();

        Assert.Equal(LiveSupplyEnd.TakenForARecording, ending.Why);
        Assert.Contains("outranks", ending.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStreamThatEndsWhileTheDriverIsDrainingSaysSo()
    {
        driver.Hello = driver.Hello with { Draining = true };
        driver.Recalled = _ => DriverCall<SessionSnapshot>.Reached(driver.Snapshot(SessionState.Stopped, SessionStopReason.Requested));

        Assert.Equal(LiveSupplyEnd.DriverDraining, (await EndedAsync()).Why);
    }

    [Fact]
    public async Task AStreamStoppedOnSomebodyElsesRequestWhileTheDriverStaysUpSaysSo()
    {
        driver.Recalled = _ => DriverCall<SessionSnapshot>.Reached(driver.Snapshot(SessionState.Stopped, SessionStopReason.Requested));

        Assert.Equal(LiveSupplyEnd.StoppedByAnother, (await EndedAsync()).Why);
    }

    [Fact]
    public async Task AStreamThatEndsWhenTheDriversLiveWindowClosesSaysSo()
    {
        driver.Recalled = _ => DriverCall<SessionSnapshot>.Reached(driver.Snapshot(SessionState.Stopped, SessionStopReason.EndTimeReached));

        LiveSupplyEnding ending = await EndedAsync();

        Assert.Equal(LiveSupplyEnd.WindowClosed, ending.Why);
        Assert.Contains("1970-01-01T04:00:00", ending.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStreamThatEndsOnATunerFaultSaysTheTunerFailed()
    {
        driver.Recalled = _ => DriverCall<SessionSnapshot>.Reached(
            driver.Snapshot(SessionState.Failed, SessionStopReason.DeviceFailed, "read: EOVERFLOW"));

        LiveSupplyEnding ending = await EndedAsync();

        Assert.Equal(LiveSupplyEnd.TunerFailed, ending.Why);
        Assert.Equal("read: EOVERFLOW", ending.Note);
    }

    [Fact]
    public async Task AStreamThatEndsWithTheDriverGoneIsLost()
    {
        LiveSupplyStart opened = await Supply().OpenAsync(Network, Service, CancellationToken.None);

        driver.Unreachable = true;

        Assert.Equal(LiveSupplyEnd.DriverLost, (await EndedAsync(opened)).Why);
    }

    [Fact]
    public async Task AStreamThatEndsWithTheDriverGoneButLastSeenDrainingIsDraining()
    {
        LiveSupplyStart opened = await Supply().OpenAsync(Network, Service, CancellationToken.None);

        driver.Unreachable = true;
        observed = DriverObservation.Of(driver.Hello, []).WhileDraining();

        Assert.Equal(LiveSupplyEnd.DriverDraining, (await EndedAsync(opened)).Why);
    }

    [Fact]
    public async Task LettingTheStreamGoStopsTheSessionWithAReasonAndAsksNothingElse()
    {
        LiveSupplyStart opened = await Supply().OpenAsync(Network, Service, CancellationToken.None);

        await opened.Stream!.DisposeAsync();

        Assert.Equal(LiveSupplyEnd.LetGo, opened.Stream.Ending!.Why);
        Assert.Equal([(driver.Held!.Value, DriverTransportStream.LetGoBecause)], driver.Stopped);
        Assert.Empty(driver.Looked);
        Assert.Equal(0, await opened.Stream.Bytes.ReadAsync(new byte[16]));
    }

    [Fact]
    public async Task AStreamTheDriverAlreadyEndedIsNotAskedToStopAgainWhenLetGo()
    {
        driver.Recalled = _ => DriverCall<SessionSnapshot>.Reached(driver.Snapshot(SessionState.Failed, SessionStopReason.Preempted));

        LiveSupplyStart opened = await Supply().OpenAsync(Network, Service, CancellationToken.None);

        await EndedAsync(opened);
        await opened.Stream!.DisposeAsync();

        Assert.Equal(LiveSupplyEnd.TakenForARecording, opened.Stream.Ending!.Why);
        Assert.Empty(driver.Stopped);
    }

    [Fact]
    public async Task ManyViewersOfOneKeyOpenOneSessionAndOneStreamOnTheDriver()
    {
        TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });
        await using LiveSessionManager sessions = new(
            new LiveSessionSettings(),
            new LiveFanoutSettings(),
            Supply(),
            new HeldTranscoders(budget),
            new HeldCaptioners(),
            new HandTurnedClock(),
            new SilentEvents());
        LiveSessionKey key = new(Network, Service, LiveProfile.Hd30);

        LiveJoin[] joined = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => sessions.JoinAsync(key, CancellationToken.None)));

        Assert.All(joined, join => Assert.True(join.Seated, join.Note));
        Assert.Single(driver.Started);
        Assert.Single(driver.Opened);
        Assert.Equal(5, sessions.Viewers(key));
    }

    private static int Remaining(int piece, int total)
        => piece switch
        {
            37 => total,
            1 => total - 37,
            700 => total - 38,
            188 => total - 738,
            _ => total - 926,
        };

    private async Task<LiveSupplyEnding> EndedAsync(LiveSupplyStart? opened = null)
    {
        LiveSupplyStart start = opened ?? await Supply().OpenAsync(Network, Service, CancellationToken.None);

        await driver.Writer.CompleteAsync();

        Assert.Equal(0, await start.Stream!.Bytes.ReadAsync(new byte[16]));

        return start.Stream.Ending!;
    }

    private DriverLiveSupply Supply(TuningResolution? resolution = null)
        => new(
            driver,
            new DeferredStatus(() => observed),
            new ServiceCollection()
                .AddSingleton<IServiceTuningDirectory>(new ResolvedTuning(
                    resolution ?? TuningResolution.Tunable(new CandidateChannelId(Guid.NewGuid()), Channel27, impaired: false)))
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>());

    private sealed class DeferredStatus(Func<DriverObservation> observed) : IDriverStatusReader
    {
        public Task<DriverObservation> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(observed());
    }
}
