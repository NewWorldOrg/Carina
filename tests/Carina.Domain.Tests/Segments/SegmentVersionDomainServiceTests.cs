using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentVersionDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Now = new(2026, 11, 1, 18, 30, 0, DateTimeKind.Utc);

    private static readonly SegmentEvaluation Evaluated = new(
        new SegmentMetrics(0.9, 0.8, [new KindMetrics(SegmentKind.Break, 0.9)], null, 0),
        [],
        new EvaluationEvidence(12, 148, Now.AddHours(-6)));

    private readonly HeldSegmentModelVersions versions = new();

    private readonly HeldSkipEvents skips = new();

    private readonly HandTurnedClock clock = new(new DateTimeOffset(Now));

    private SegmentVersionDomainService Service => new(versions, skips, clock);

    [Fact(DisplayName = "BR-CD-008: with no version used before the one in use, nothing is rolled back and the version in use is the round's")]
    public async Task WithNothingBeforeNothingIsRolledBack()
    {
        SegmentVersionInUse inUse = await Service.RollBackWhenDueAsync(Now, Cancel);

        Assert.Null(inUse.Rollback);
        Assert.Equal(versions.Active.Number, inUse.Active.Number);
        Assert.Empty(versions.Written);
    }

    [Fact(DisplayName = "BR-CD-008: viewers coming back from more than one and a half times the share of skips roll the version in use back through the one change, at the moment it is made")]
    public async Task ManyReturnsRollTheVersionInUseBack()
    {
        DateTime adopted = await SecondInUseAsync(daysAgo: 3);
        await SkippedAsync(2, adopted.AddHours(1), skips: 25, returned: 10);
        await SkippedAsync(1, adopted.AddDays(-3), skips: 40, returned: 4);
        clock.Turn(TimeSpan.FromMinutes(5));

        SegmentVersionInUse inUse = await Service.RollBackWhenDueAsync(Now, Cancel);

        Assert.Equal(new SegmentRollback(0.4, 0.1, 25, 1), inUse.Rollback);
        Assert.Equal(1, inUse.Active.Number);
        Assert.Equal(Now.AddMinutes(5), versions.Others.Single(version => version.Number == 2).RolledBackAt);
    }

    [Fact(DisplayName = "BR-CD-008: viewers coming back from no more than one and a half times the share leave the version in use")]
    public async Task FewReturnsLeaveTheVersionInUse()
    {
        DateTime adopted = await SecondInUseAsync(daysAgo: 3);
        await SkippedAsync(2, adopted.AddHours(1), skips: 25, returned: 3);
        await SkippedAsync(1, adopted.AddDays(-3), skips: 40, returned: 4);

        SegmentVersionInUse inUse = await Service.RollBackWhenDueAsync(Now, Cancel);

        Assert.Null(inUse.Rollback);
        Assert.Equal(2, inUse.Active.Number);
    }

    [Fact(DisplayName = "BR-CS-003: a candidate kept is numbered after every number given, lets go of the candidates before it that nothing names, and is judged with the rollbacks of the last days")]
    public async Task ACandidateKeptLetsGoOfThoseBeforeIt()
    {
        versions.Others.Add(SegmentModelVersion.Learned(2, HandMadeModels.First, Evaluated, Now.AddDays(-2)));
        versions.Others.Add(SegmentModelVersion.Learned(3, HandMadeModels.First, Evaluated, Now.AddDays(-1)));
        versions.Named.Add(3);

        SegmentCandidateKept kept = await Service.KeepCandidateAsync(Learned(AdoptionVerdict.Adopted), Cancel);

        Assert.Equal((4, Now), (kept.Candidate.Number, kept.Candidate.TrainedAt));
        Assert.Equal([2], kept.LetGo);
        Assert.Equal(AdoptionVerdict.Adopted, kept.Verdict);
        Assert.Equal([1, 3, 4], versions.Others.Select(version => version.Number).Prepend(versions.Active.Number).Order());
    }

    [Fact(DisplayName = "BR-CD-008: a candidate that would be put in use within seven days of a rollback is held out of use")]
    public async Task ACandidateAfterARollbackIsHeld()
    {
        DateTime adopted = await SecondInUseAsync(daysAgo: 3);
        await SkippedAsync(2, adopted.AddHours(1), skips: 25, returned: 10);
        await SkippedAsync(1, adopted.AddDays(-3), skips: 40, returned: 4);
        await Service.RollBackWhenDueAsync(Now, Cancel);

        SegmentCandidateKept kept = await Service.KeepCandidateAsync(Learned(AdoptionVerdict.Adopted), Cancel);

        Assert.Equal(AdoptionVerdict.HeldAfterRollback, kept.Verdict);
    }

    [Fact(DisplayName = "BR-CD-008: a candidate judged better is put in use while the version in use is the one it was judged against")]
    public async Task ACandidateIsPutInUseAgainstTheVersionItWasJudgedAgainst()
    {
        SegmentModelVersion judgedAgainst = versions.Active;
        SegmentCandidateKept kept = await Service.KeepCandidateAsync(Learned(AdoptionVerdict.Adopted), Cancel);

        SegmentCandidateAdoption adoption = await Service.AdoptAsync(kept.Candidate, judgedAgainst, Cancel);

        Assert.True(adoption.Adopted);
        Assert.Equal(kept.Candidate.Number, adoption.InUse.Number);
        Assert.Equal(kept.Candidate.Number, versions.Active.Number);
    }

    [Fact(DisplayName = "BR-CD-008: a candidate is not put in use when the version in use changed since it was judged, or was chosen by hand meanwhile")]
    public async Task ACandidateIsNotPutInUseWhenTheVersionInUseMoved()
    {
        SegmentModelVersion judgedAgainst = versions.Active;
        SegmentCandidateKept kept = await Service.KeepCandidateAsync(Learned(AdoptionVerdict.Adopted), Cancel);
        await versions.ChangeAsync(SegmentVersionChange.ChosenByHand(judgedAgainst.Number), Now, Cancel);

        SegmentCandidateAdoption byHand = await Service.AdoptAsync(kept.Candidate, judgedAgainst, Cancel);
        SegmentCandidateAdoption moved = await Service.AdoptAsync(kept.Candidate, kept.Candidate, Cancel);

        Assert.False(byHand.Adopted);
        Assert.False(moved.Adopted);
        Assert.Equal(judgedAgainst.Number, byHand.InUse.Number);
        Assert.Equal(judgedAgainst.Number, versions.Active.Number);
    }

    [Theory(DisplayName = "BR-CS-003: learning lets go of a candidate numbered before the latest that was never used, is not built in and that nothing names, and of no other version")]
    [InlineData(4, false, false, true)]
    [InlineData(4, true, false, false)]
    [InlineData(4, false, true, false)]
    [InlineData(5, false, false, false)]
    [InlineData(6, false, false, false)]
    public void ACandidateIsLetGoOnlyWhenUnusedAndUnnamed(int number, bool named, bool builtIn, bool letGo)
    {
        SegmentModelVersion candidate = SegmentModelVersion.Rehydrate(number, SegmentModelVersionState.Candidate, false, HandMadeModels.First, Now, null, null, null, null, builtIn: builtIn);
        SegmentModelVersion used = SegmentModelVersion.Rehydrate(number, SegmentModelVersionState.Retired, false, HandMadeModels.First, Now, Now, Now.AddDays(1), null, null);

        Assert.Equal(letGo, candidate.IsLetGoBefore(5, named));
        Assert.False(used.IsLetGoBefore(5, named));
        Assert.False(versions.Active.IsLetGoBefore(5, named));
    }

    private static SegmentLearned Learned(AdoptionVerdict verdict) => new(HandMadeModels.First, Evaluated, Evaluated, verdict);

    private async Task<DateTime> SecondInUseAsync(int daysAgo)
    {
        DateTime adopted = Now.AddDays(-daysAgo);

        versions.Others.Add(SegmentModelVersion.Rehydrate(2, SegmentModelVersionState.Candidate, false, HandMadeModels.First, adopted.AddHours(-1), null, null, null, null));
        await versions.ChangeAsync(SegmentVersionChange.Adopting(2), adopted, Cancel);
        versions.Written.Clear();

        return adopted;
    }

    private async Task SkippedAsync(int version, DateTime from, int skips, int returned)
    {
        RecordingId recording = RecordingId.New();

        for (int at = 0; at < skips; at++)
        {
            await this.skips.TakeAsync(Skip(recording, ViewerActionKind.Skipped, version, from.AddMinutes(at)), Cancel);
        }

        for (int at = 0; at < returned; at++)
        {
            await this.skips.TakeAsync(Skip(recording, ViewerActionKind.Returned, version, from.AddMinutes(at).AddSeconds(30)), Cancel);
        }
    }

    private static SkipEvent Skip(RecordingId recording, ViewerActionKind action, int version, DateTime at)
        => SkipEvent.Of(recording, new ViewerAction(at, action, SegmentKind.Break, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690), version));
}
