using Carina.Contracts;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

namespace Carina.Driver.Tests;

public sealed class TunerPoolTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly string[] OneTuner = ["adapter0"];

    private static readonly string[] TwoTuners = ["adapter0", "adapter1"];

    private static readonly TuningKey Here = new(TunerKind.Terrestrial, 55, 0);

    private static readonly TuningKey Elsewhere = new(TunerKind.Terrestrial, 57, 0);

    private readonly ManualTimeProvider clock = new(Start);

    private TunerPool Pool(TimeSpan? grace = null) => new(clock, grace);

    private static PoolRequest Wanting(
        string sessionId,
        SessionPurpose purpose,
        TuningKey? tuning = null,
        string? deviceId = null,
        IReadOnlyList<string>? candidates = null
    ) =>
        new(
            SessionId.Parse(sessionId),
            purpose,
            tuning ?? Here,
            deviceId,
            candidates ?? OneTuner
        );

    private static PoolGrant Take(
        TunerPool pool,
        string sessionId,
        SessionPurpose purpose,
        TuningKey? tuning = null,
        string? deviceId = null,
        IReadOnlyList<string>? candidates = null
    )
    {
        var grant = pool.Acquire(Wanting(sessionId, purpose, tuning, deviceId, candidates));

        if (grant.Verdict is PoolVerdict.Granted && grant.NeedsTuning)
        {
            pool.Tuned(grant.DeviceId, new FakeTunerDevice(55));
        }

        if (grant.Verdict is PoolVerdict.Granted)
        {
            pool.Ready(grant.DeviceId);
        }

        return grant;
    }

    [Fact]
    public void TheFirstConsumerOfATuningOpensATuner()
    {
        var pool = Pool();

        var grant = Take(pool, "s-1", SessionPurpose.Live);

        Assert.Equal(PoolVerdict.Granted, grant.Verdict);
        Assert.Equal("adapter0", grant.DeviceId);
        Assert.True(grant.NeedsTuning);
        Assert.Empty(grant.Displaced);
    }

    [Fact]
    public void TwoConsumersOfTheSameTuningRideOneTuner()
    {
        var pool = Pool();

        Take(pool, "s-1", SessionPurpose.Live);
        var second = pool.Acquire(Wanting("s-2", SessionPurpose.Live, candidates: TwoTuners));

        Assert.Equal(PoolVerdict.Shared, second.Verdict);
        Assert.Equal("adapter0", second.DeviceId);
        Assert.Equal(SessionId.Parse("s-1"), second.Holder);
        Assert.False(second.NeedsTuning);
    }

    [Fact]
    public void RidingAlongIsPreferredToOpeningASecondTuner()
    {
        var pool = Pool();

        Take(pool, "s-1", SessionPurpose.Live, candidates: TwoTuners);
        var second = pool.Acquire(Wanting("s-2", SessionPurpose.Live, candidates: TwoTuners));

        Assert.Equal(PoolVerdict.Shared, second.Verdict);
        Assert.False(pool.IsHeld("adapter1"));
    }

    [Fact]
    public void AConsumerOfAnotherTuningTakesATunerOfItsOwn()
    {
        var pool = Pool();

        Take(pool, "s-1", SessionPurpose.Live, candidates: TwoTuners);
        var second = Take(
            pool,
            "s-2",
            SessionPurpose.Live,
            Elsewhere,
            candidates: TwoTuners
        );

        Assert.Equal(PoolVerdict.Granted, second.Verdict);
        Assert.Equal("adapter1", second.DeviceId);
        Assert.True(second.NeedsTuning);
    }

    [Fact]
    public void TheTwoStreamsOnOneSatelliteChannelAreNotTheSameTuning()
    {
        var pool = Pool();
        var one = TuningKey.Of(TuneParams.Bs(15, 50001));
        var other = TuningKey.Of(TuneParams.Bs(15, 50002));

        Take(pool, "s-1", SessionPurpose.Live, one, candidates: TwoTuners);
        var second = pool.Acquire(Wanting("s-2", SessionPurpose.Live, other, candidates: TwoTuners));

        Assert.NotEqual(PoolVerdict.Shared, second.Verdict);
        Assert.Equal("adapter1", second.DeviceId);
    }

    [Fact]
    public void ARequestForANamedTunerNeverRidesAlongOnAnother()
    {
        var pool = Pool();

        Take(pool, "s-1", SessionPurpose.Live, candidates: TwoTuners);
        var second = Take(
            pool,
            "s-2",
            SessionPurpose.Live,
            deviceId: "adapter1",
            candidates: TwoTuners
        );

        Assert.Equal(PoolVerdict.Granted, second.Verdict);
        Assert.Equal("adapter1", second.DeviceId);
    }

    [Fact]
    public void AnEqualPriorityRequestNeverDisplacesAnEqual()
    {
        var pool = Pool();

        Take(pool, "s-1", SessionPurpose.Recording);
        var second = pool.Acquire(Wanting("s-2", SessionPurpose.Recording, Elsewhere));

        Assert.Equal(PoolVerdict.NoDeviceFree, second.Verdict);
        Assert.Empty(second.Displaced);
        Assert.Equal([SessionId.Parse("s-1")], pool.SinksOn("adapter0"));
    }

    [Theory]
    [InlineData(SessionPurpose.Recording)]
    [InlineData(SessionPurpose.Live)]
    [InlineData(SessionPurpose.Scan)]
    [InlineData(SessionPurpose.Survey)]
    public void NoPurposeDisplacesItself(SessionPurpose purpose)
    {
        var pool = Pool();

        Take(pool, "s-1", purpose);
        var second = pool.Acquire(Wanting("s-2", purpose, Elsewhere));

        Assert.Equal(PoolVerdict.NoDeviceFree, second.Verdict);
        Assert.Empty(second.Displaced);
    }

    [Theory]
    [InlineData(SessionPurpose.Recording, SessionPurpose.Live)]
    [InlineData(SessionPurpose.Recording, SessionPurpose.Scan)]
    [InlineData(SessionPurpose.Recording, SessionPurpose.Survey)]
    [InlineData(SessionPurpose.Live, SessionPurpose.Scan)]
    [InlineData(SessionPurpose.Live, SessionPurpose.Survey)]
    [InlineData(SessionPurpose.Scan, SessionPurpose.Survey)]
    public void TheMoreImportantReasonTakesTheTunerFromTheLesser(
        SessionPurpose winner,
        SessionPurpose loser
    )
    {
        var pool = Pool();

        Take(pool, "s-1", loser);
        var second = pool.Acquire(Wanting("s-2", winner, Elsewhere));

        Assert.Equal(PoolVerdict.Granted, second.Verdict);
        Assert.Equal("adapter0", second.DeviceId);
        Assert.True(second.NeedsTuning);
        Assert.Equal([SessionId.Parse("s-1")], second.Displaced);
        Assert.Equal([SessionId.Parse("s-2")], pool.SinksOn("adapter0"));
    }

    [Theory]
    [InlineData(SessionPurpose.Live, SessionPurpose.Recording)]
    [InlineData(SessionPurpose.Scan, SessionPurpose.Recording)]
    [InlineData(SessionPurpose.Survey, SessionPurpose.Recording)]
    [InlineData(SessionPurpose.Scan, SessionPurpose.Live)]
    [InlineData(SessionPurpose.Survey, SessionPurpose.Live)]
    [InlineData(SessionPurpose.Survey, SessionPurpose.Scan)]
    public void TheLesserReasonWaitsRatherThanTakingTheTuner(
        SessionPurpose asking,
        SessionPurpose holding
    )
    {
        var pool = Pool();

        Take(pool, "s-1", holding);
        var second = pool.Acquire(Wanting("s-2", asking, Elsewhere));

        Assert.Equal(PoolVerdict.NoDeviceFree, second.Verdict);
        Assert.Empty(second.Displaced);
        Assert.Equal([SessionId.Parse("s-1")], pool.SinksOn("adapter0"));
    }

    [Fact]
    public void TheLeastImportantHolderIsTheOneThatLosesItsTuner()
    {
        var pool = Pool();

        Take(pool, "s-1", SessionPurpose.Scan, candidates: TwoTuners);
        Take(pool, "s-2", SessionPurpose.Survey, Elsewhere, candidates: TwoTuners);

        var third = pool.Acquire(
            Wanting("s-3", SessionPurpose.Recording, new TuningKey(TunerKind.Terrestrial, 53, 0), candidates: TwoTuners)
        );

        Assert.Equal("adapter1", third.DeviceId);
        Assert.Equal([SessionId.Parse("s-2")], third.Displaced);
        Assert.Equal([SessionId.Parse("s-1")], pool.SinksOn("adapter0"));
    }

    [Fact]
    public void ATunerIsJudgedByItsMostImportantRiderAndNotItsLeast()
    {
        var pool = Pool();

        Take(pool, "s-1", SessionPurpose.Survey);
        pool.Acquire(Wanting("s-2", SessionPurpose.Recording));

        var third = pool.Acquire(Wanting("s-3", SessionPurpose.Live, Elsewhere));

        Assert.Equal(PoolVerdict.NoDeviceFree, third.Verdict);
        Assert.Empty(third.Displaced);
    }

    [Fact]
    public void EveryRiderOfADisplacedTunerIsNamedInTheGrant()
    {
        var pool = Pool();

        Take(pool, "s-1", SessionPurpose.Survey);
        pool.Acquire(Wanting("s-2", SessionPurpose.Survey));

        var third = pool.Acquire(Wanting("s-3", SessionPurpose.Recording, Elsewhere));

        Assert.Equal([SessionId.Parse("s-1"), SessionId.Parse("s-2")], third.Displaced);
        Assert.Equal([SessionId.Parse("s-3")], pool.SinksOn("adapter0"));
    }

    [Fact]
    public void TheGrantThatDisplacesSomeoneSaysWhoTookTheTunerAndWhatFor()
    {
        var pool = Pool();

        Take(pool, "s-1", SessionPurpose.Survey);

        var second = pool.Acquire(Wanting("s-2", SessionPurpose.Recording, Elsewhere));

        Assert.Contains("s-2", second.Detail, StringComparison.Ordinal);
        Assert.Contains("recording", second.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("s-1", second.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusalOnANamedTunerSaysThatTunerIsBusyRatherThanThatNoneIsFree()
    {
        var pool = Pool();

        Take(pool, "s-1", SessionPurpose.Recording, candidates: TwoTuners);

        var second = pool.Acquire(
            Wanting("s-2", SessionPurpose.Recording, Elsewhere, "adapter0", TwoTuners)
        );

        Assert.Equal(PoolVerdict.DeviceBusy, second.Verdict);
        Assert.Contains("adapter0", second.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTunerIsHeldForAWhileAfterTheLastRiderLeaves()
    {
        var pool = Pool(TimeSpan.FromSeconds(5));

        Take(pool, "s-1", SessionPurpose.Live);
        pool.Leave(SessionId.Parse("s-1"));

        clock.Advance(TimeSpan.FromSeconds(4));

        var second = pool.Acquire(Wanting("s-2", SessionPurpose.Live, Elsewhere, candidates: TwoTuners));

        Assert.Equal("adapter1", second.DeviceId);
        Assert.True(pool.IsLingering("adapter0"));
    }

    [Fact]
    public void ARiderComingStraightBackPaysNoRetune()
    {
        var pool = Pool(TimeSpan.FromSeconds(5));

        Take(pool, "s-1", SessionPurpose.Live);
        pool.Leave(SessionId.Parse("s-1"));

        clock.Advance(TimeSpan.FromSeconds(4));

        var second = pool.Acquire(Wanting("s-2", SessionPurpose.Live));

        Assert.Equal(PoolVerdict.Granted, second.Verdict);
        Assert.Equal("adapter0", second.DeviceId);
        Assert.False(second.NeedsTuning);
        Assert.Equal(SessionId.Parse("s-2"), second.Holder);
    }

    [Fact]
    public void TheTunerHeldOverIsTheSameOpenTunerAndNotAFreshOne()
    {
        var pool = Pool(TimeSpan.FromSeconds(5));
        var device = new FakeTunerDevice(55);

        var first = pool.Acquire(Wanting("s-1", SessionPurpose.Live));
        pool.Tuned(first.DeviceId, device);
        pool.Ready(first.DeviceId);
        pool.Leave(SessionId.Parse("s-1"));

        clock.Advance(TimeSpan.FromSeconds(4));
        pool.Acquire(Wanting("s-2", SessionPurpose.Live));

        Assert.Same(device, pool.DeviceOf("adapter0"));
    }

    [Fact]
    public void TheHeldTunerGoesBackOnceTheGraceHasRunOut()
    {
        var pool = Pool(TimeSpan.FromSeconds(5));

        Take(pool, "s-1", SessionPurpose.Live);
        pool.Leave(SessionId.Parse("s-1"));

        clock.Advance(TimeSpan.FromSeconds(6));

        var second = pool.Acquire(Wanting("s-2", SessionPurpose.Live));

        Assert.Equal(PoolVerdict.Granted, second.Verdict);
        Assert.True(second.NeedsTuning);
        Assert.False(pool.IsLingering("adapter0"));
    }

    [Fact]
    public void AHeldTunerIsGivenUpRatherThanRefusingTheOnlyOtherTuning()
    {
        var pool = Pool(TimeSpan.FromSeconds(5));

        Take(pool, "s-1", SessionPurpose.Live);
        pool.Leave(SessionId.Parse("s-1"));

        var second = pool.Acquire(Wanting("s-2", SessionPurpose.Survey, Elsewhere));

        Assert.Equal(PoolVerdict.Granted, second.Verdict);
        Assert.Equal("adapter0", second.DeviceId);
        Assert.True(second.NeedsTuning);
        Assert.Empty(second.Displaced);
    }

    [Fact]
    public void TheHeldTunerIsClosedWhenSomeoneElseTunesIt()
    {
        var pool = Pool(TimeSpan.FromSeconds(5));
        var device = new ClosableTunerDevice();

        var first = pool.Acquire(Wanting("s-1", SessionPurpose.Live));
        pool.Tuned(first.DeviceId, device);
        pool.Ready(first.DeviceId);
        pool.Leave(SessionId.Parse("s-1"));

        pool.Acquire(Wanting("s-2", SessionPurpose.Live, Elsewhere));
        pool.HandOver("adapter0");

        Assert.True(device.Disposed);
    }

    [Fact]
    public void ATunerThatCouldNotBeTunedIsNotHandedStraightBackOut()
    {
        var pool = Pool(TimeSpan.FromSeconds(5));

        var first = pool.Acquire(Wanting("s-1", SessionPurpose.Live));
        pool.TuningFailed(first.DeviceId, new IOException("the frontend would not lock"));

        var second = pool.Acquire(Wanting("s-2", SessionPurpose.Live));

        Assert.Equal(PoolVerdict.NoDeviceFree, second.Verdict);
        Assert.Empty(pool.SinksOn("adapter0"));
        Assert.False(pool.IsHeld("adapter0"));
    }

    [Fact]
    public void ATunerThatCouldNotBeTunedIsNotOfferedAsARideEither()
    {
        var pool = Pool(TimeSpan.FromSeconds(5));

        var first = pool.Acquire(Wanting("s-1", SessionPurpose.Live));
        pool.TuningFailed(first.DeviceId, new IOException("the frontend would not lock"));

        var second = pool.Acquire(Wanting("s-2", SessionPurpose.Recording));

        Assert.NotEqual(PoolVerdict.Shared, second.Verdict);
        Assert.NotEqual(PoolVerdict.Granted, second.Verdict);
    }

    [Fact]
    public void TheRefusalAfterAFailedTuneRepeatsWhatWentWrong()
    {
        var pool = Pool(TimeSpan.FromSeconds(5));

        var first = pool.Acquire(Wanting("s-1", SessionPurpose.Live));
        pool.TuningFailed(first.DeviceId, new IOException("the frontend would not lock"));

        var second = pool.Acquire(Wanting("s-2", SessionPurpose.Live));

        Assert.Contains("adapter0", second.Detail, StringComparison.Ordinal);
        Assert.Contains("would not lock", second.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ATunerThatCouldNotBeTunedComesBackOnceItsHoldRunsOut()
    {
        var pool = Pool(TimeSpan.FromSeconds(5));

        var first = pool.Acquire(Wanting("s-1", SessionPurpose.Live));
        pool.TuningFailed(first.DeviceId, new IOException("the frontend would not lock"));

        clock.Advance(TimeSpan.FromSeconds(6));

        var second = pool.Acquire(Wanting("s-2", SessionPurpose.Live));

        Assert.Equal(PoolVerdict.Granted, second.Verdict);
        Assert.True(second.NeedsTuning);
    }

    [Fact]
    public void AFailedTuneNeverLeavesAWaiterHangingOnAHolderThatWillNotCome()
    {
        var pool = Pool(TimeSpan.FromSeconds(5));

        var first = pool.Acquire(Wanting("s-1", SessionPurpose.Live));
        pool.TuningFailed(first.DeviceId, new IOException("the frontend would not lock"));

        Assert.False(pool.AwaitReady("adapter0", TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ARiderWaitsForTheHolderItWasToldToRideRatherThanReadingNothing()
    {
        using var pool = Pool();
        var answered = new ManualResetEventSlim(false);
        var seated = new ManualResetEventSlim(false);

        var first = pool.Acquire(Wanting("s-1", SessionPurpose.Live));

        var verdict = PoolVerdict.NoDeviceFree;
        var rode = false;

        var rider = new Thread(() =>
        {
            var grant = pool.Acquire(Wanting("s-2", SessionPurpose.Live));
            verdict = grant.Verdict;
            answered.Set();
            rode = pool.AwaitReady(grant.DeviceId, Deadlock);
            seated.Set();
        })
        {
            IsBackground = true,
        };

        rider.Start();

        Assert.True(
            answered.Wait(Deadlock),
            "The rider never got its verdict while the holder was still tuning."
        );
        Assert.Equal(PoolVerdict.Shared, verdict);
        Assert.False(seated.IsSet);

        pool.Tuned(first.DeviceId, new FakeTunerDevice(55));
        pool.Ready(first.DeviceId);

        Assert.True(seated.Wait(Deadlock));
        Assert.True(rode);
        Assert.True(rider.Join(Deadlock));
    }

    [Fact]
    public void OneTuneInFlightDoesNotHoldUpARequestForAnotherTuner()
    {
        using var pool = Pool();
        var answered = new ManualResetEventSlim(false);

        var first = pool.Acquire(Wanting("s-1", SessionPurpose.Live, candidates: TwoTuners));

        var taken = string.Empty;

        var asking = new Thread(() =>
        {
            taken = pool
                .Acquire(Wanting("s-2", SessionPurpose.Live, Elsewhere, candidates: TwoTuners))
                .DeviceId;
            answered.Set();
        })
        {
            IsBackground = true,
        };

        asking.Start();

        Assert.True(
            answered.Wait(Deadlock),
            "A second request never got an answer while the first was still tuning."
        );
        Assert.Equal("adapter1", taken);

        pool.Tuned(first.DeviceId, new FakeTunerDevice(55));
        pool.Ready(first.DeviceId);

        Assert.True(asking.Join(Deadlock));
    }

    [Fact]
    public void ATunerTakenOutOfServiceIsForgottenRatherThanLingering()
    {
        var pool = Pool(TimeSpan.FromSeconds(5));
        var device = new ClosableTunerDevice();

        var first = pool.Acquire(Wanting("s-1", SessionPurpose.Live));
        pool.Tuned(first.DeviceId, device);
        pool.Ready(first.DeviceId);
        pool.Leave(SessionId.Parse("s-1"));
        pool.Discard("adapter0");

        Assert.False(pool.IsLingering("adapter0"));
        Assert.True(device.Disposed);
    }

    [Fact]
    public void ClosingThePoolClosesEveryTunerItStillHolds()
    {
        var pool = Pool(TimeSpan.FromSeconds(5));
        var device = new ClosableTunerDevice();

        var first = pool.Acquire(Wanting("s-1", SessionPurpose.Live));
        pool.Tuned(first.DeviceId, device);
        pool.Ready(first.DeviceId);

        pool.Dispose();

        Assert.True(device.Disposed);
    }

    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);
}

public sealed class ClosableTunerDevice : ITunerDevice
{
    private readonly FakeTunerDevice inner = new(55, 50001);

    public long Overflows => 0;

    public bool Disposed { get; private set; }

    public byte[] Read(int count, CancellationToken cancellationToken) =>
        inner.Read(count, cancellationToken);

    public void Dispose() => Disposed = true;
}
