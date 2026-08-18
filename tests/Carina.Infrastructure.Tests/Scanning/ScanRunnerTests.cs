using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Scanning;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Scanning;

public sealed class ScanRunnerTests : IAsyncLifetime
{
    private readonly HeldScanRuns runs = new();
    private readonly ServiceProvider provider;

    public ScanRunnerTests()
    {
        Orchestrator = new ScriptedScanOrchestrator(runs);
        provider = new ServiceCollection()
            .AddSingleton<IChannelScanOrchestrator>(Orchestrator)
            .AddSingleton<IScanRunRepository>(runs)
            .AddSingleton(TimeProvider.System)
            .BuildServiceProvider();
        Runner = new ScanRunner(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScanRunner>.Instance);
    }

    private ScriptedScanOrchestrator Orchestrator { get; }

    private ScanRunner Runner { get; }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await Runner.StopAsync(CancellationToken.None);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task StartingAScanAnswersWithItsIdentityBeforeTheWalkIsOver()
    {
        Orchestrator.HoldsOpen = true;

        ScanLaunch launch = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        Assert.True(launch.WasStarted);
        Assert.Equal(runs.Runs[0].Id, launch.Started);
        Assert.True(runs.Runs[0].IsRunning);
    }

    [Fact]
    public async Task ASecondScanIsRefusedAndNamesTheOneAlreadyWalking()
    {
        Orchestrator.HoldsOpen = true;

        ScanLaunch first = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);
        ScanLaunch second = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        Assert.False(second.WasStarted);
        Assert.Equal(first.Started, second.AlreadyRunning);
    }

    [Fact]
    public async Task AScanThatCannotStartSaysWhyRatherThanHandingOutAnIdentity()
    {
        Orchestrator.CouldNotStart = "the driver did not answer";

        ScanLaunch launch = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        Assert.False(launch.WasStarted);
        Assert.Null(launch.AlreadyRunning);
        Assert.Equal("the driver did not answer", launch.CouldNotStartBecause);
    }

    [Fact]
    public async Task CancellingAWalkingScanEndsItAsCancelled()
    {
        Orchestrator.HoldsOpen = true;

        ScanLaunch launch = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        Assert.True(Runner.TryCancel(launch.Started!));

        await Eventually.Happens(
            () => !Runner.IsWalking(launch.Started!),
            "the runner lets go of the scan it was asked to stop");

        Assert.Equal(ScanRunState.Cancelled, runs.Runs[0].State);
    }

    [Fact]
    public async Task CancellingAScanNobodyIsWalkingIsRefused()
    {
        ScanLaunch launch = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        await Eventually.Happens(
            () => !Runner.IsWalking(launch.Started!),
            "the scan finishes on its own");

        Assert.False(Runner.TryCancel(launch.Started!));
    }

    [Fact]
    public async Task ACompletedScanLeavesTheDifferenceItProposedWhereApplyCanFindIt()
    {
        Orchestrator.Difference = ProposedDifference();

        ScanLaunch launch = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        await Eventually.Happens(
            () => Runner.TryPeekProposal(launch.Started!, out _),
            "the proposal is held for the apply that follows");

        ProposalClaim.Claimed claim = Assert.IsType<ProposalClaim.Claimed>(Runner.ClaimProposal(launch.Started!));
        Assert.Single(claim.Proposal.Difference.Added);
    }

    [Fact]
    public async Task AProposalIsHeldByOneApplyAtATimeSoTwoCannotWriteIt()
    {
        Orchestrator.Difference = ProposedDifference();

        ScanLaunch launch = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        await Eventually.Happens(
            () => Runner.TryPeekProposal(launch.Started!, out _),
            "the proposal is held");

        Assert.IsType<ProposalClaim.Claimed>(Runner.ClaimProposal(launch.Started!));

        Assert.IsType<ProposalClaim.AlreadyBeingApplied>(Runner.ClaimProposal(launch.Started!));
    }

    [Fact]
    public async Task AProposalWhoseApplyDidNotLandCanBeAppliedAgainWithoutWalkingAgain()
    {
        Orchestrator.Difference = ProposedDifference();

        ScanLaunch launch = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        await Eventually.Happens(
            () => Runner.TryPeekProposal(launch.Started!, out _),
            "the proposal is held");

        ProposalClaim.Claimed held = Assert.IsType<ProposalClaim.Claimed>(Runner.ClaimProposal(launch.Started!));
        Runner.GiveBackProposal(launch.Started!, held.Hold);

        ProposalClaim.Claimed again = Assert.IsType<ProposalClaim.Claimed>(Runner.ClaimProposal(launch.Started!));
        Assert.Single(again.Proposal.Difference.Added);
    }

    [Fact]
    public async Task AProposalWhoseApplyLandedIsNotOfferedASecondTime()
    {
        Orchestrator.Difference = ProposedDifference();

        ScanLaunch launch = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        await Eventually.Happens(
            () => Runner.TryPeekProposal(launch.Started!, out _),
            "the proposal is held");

        ProposalClaim.Claimed applied = Assert.IsType<ProposalClaim.Claimed>(Runner.ClaimProposal(launch.Started!));
        Runner.ProposalApplied(launch.Started!, applied.Hold);

        Assert.IsType<ProposalClaim.Gone>(Runner.ClaimProposal(launch.Started!));
        Assert.IsType<ProposalClaim.Gone>(Runner.ClaimProposal(launch.Started!));
        Assert.False(Runner.TryPeekProposal(launch.Started!, out _));
    }

    [Fact]
    public async Task AProposalIsNotReleasedByAnyoneOtherThanTheApplyHoldingIt()
    {
        Orchestrator.Difference = ProposedDifference();

        ScanLaunch launch = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        await Eventually.Happens(
            () => Runner.TryPeekProposal(launch.Started!, out _),
            "the proposal is held");

        Assert.IsType<ProposalClaim.Claimed>(Runner.ClaimProposal(launch.Started!));

        Runner.GiveBackProposal(launch.Started!, Guid.NewGuid());
        Runner.ProposalApplied(launch.Started!, Guid.NewGuid());

        Assert.IsType<ProposalClaim.AlreadyBeingApplied>(Runner.ClaimProposal(launch.Started!));
        Assert.True(Runner.TryPeekProposal(launch.Started!, out _));
    }

    [Fact]
    public async Task ACancelledScanProposesNothingToApply()
    {
        Orchestrator.HoldsOpen = true;
        Orchestrator.Difference = ProposedDifference();

        ScanLaunch launch = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);
        Runner.TryCancel(launch.Started!);

        await Eventually.Happens(
            () => !Runner.IsWalking(launch.Started!),
            "the cancelled scan is let go");

        Assert.False(Runner.TryPeekProposal(launch.Started!, out _));
    }

    [Fact]
    public async Task TheProposalRemembersWhichSystemsTheScanActuallyWalked()
    {
        Orchestrator.Walked.Add(TuningParameters.Terrestrial(53));
        Orchestrator.Difference = ProposedDifference();

        ScanLaunch launch = await Runner.LaunchAsync(
            ScanScope.Of(TuneSystem.IsdbT),
            CancellationToken.None);

        await Eventually.Happens(
            () => Runner.TryPeekProposal(launch.Started!, out _),
            "the proposal is held");

        Assert.True(Runner.TryPeekProposal(launch.Started!, out ScanProposal? proposal));
        Assert.Equal([TuneSystem.IsdbT], proposal.Systems);
    }

    [Fact]
    public async Task TheScopeAskedForIsTheScopeWalked()
    {
        await Runner.LaunchAsync(ScanScope.Of(TuneSystem.IsdbSCs110), CancellationToken.None);

        await Eventually.Happens(
            () => Orchestrator.Scopes.Count == 1,
            "the orchestrator was handed a scope");

        Assert.Equal([TuneSystem.IsdbSCs110], Orchestrator.Scopes[0].Systems);
    }

    [Fact]
    public async Task AWalkThatThrowsAfterAnnouncingSettlesTheRunInsteadOfLeavingItRunning()
    {
        Orchestrator.ThrowsAfterAnnouncing = "the database went away mid-walk";

        ScanLaunch launch = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        await Eventually.Happens(
            () => !Runner.IsWalking(launch.Started!),
            "the runner lets go of the scan that threw");

        Assert.Equal(ScanRunState.Failed, runs.Runs[0].State);
        Assert.False(string.IsNullOrWhiteSpace(runs.Runs[0].Reason));
    }

    [Fact]
    public async Task AWalkThatThrowsAfterAnnouncingDoesNotWedgeEveryLaterScan()
    {
        Orchestrator.ThrowsAfterAnnouncing = "the database went away mid-walk";

        ScanLaunch wedged = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        await Eventually.Happens(
            () => !Runner.IsWalking(wedged.Started!),
            "the runner lets go of the scan that threw");

        Orchestrator.ThrowsAfterAnnouncing = null;

        ScanLaunch next = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        Assert.True(next.WasStarted);
        Assert.Null(next.AlreadyRunning);
    }

    [Fact]
    public async Task StoppingTheAppWaitsForTheWalkRatherThanLeavingItToRaceTeardown()
    {
        Orchestrator.HoldsOpen = true;

        ScanLaunch launch = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        await Runner.StopAsync(CancellationToken.None);

        Assert.False(Runner.IsWalking(launch.Started!));
        Assert.False(runs.Runs[0].IsRunning);
    }

    [Fact]
    public async Task AScanStoppedByTheAppIsNotRecordedAsSomethingTheOperatorDid()
    {
        Orchestrator.HoldsOpen = true;

        await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);
        await Runner.StopAsync(CancellationToken.None);

        Assert.Equal(ScanRunState.Failed, runs.Runs[0].State);
        Assert.Equal(ScanConclusion.AppStoppingReason, runs.Runs[0].Reason);
    }

    [Fact]
    public async Task AScanLeftRunningByAHardKillIsSettledWhenTheAppComesBack()
    {
        var orphan = ScanRun.Start(ScanRunId.New(), "instance-a", ScriptedScanOrchestrator.At);
        await runs.StartAsync(orphan, CancellationToken.None);

        await Runner.StartAsync(CancellationToken.None);

        Assert.Equal(ScanRunState.Failed, orphan.State);
        Assert.Equal(ScanConclusion.AbandonedReason, orphan.Reason);

        ScanLaunch next = await Runner.LaunchAsync(ScanScope.Everything, CancellationToken.None);

        Assert.True(next.WasStarted);
    }

    [Fact]
    public async Task TheProposalCountsASystemAsWalkedEvenWhenEveryAttemptOnItFailed()
    {
        Orchestrator.Walked.Add(TuningParameters.Terrestrial(53));
        Orchestrator.EveryAttemptFails = true;
        Orchestrator.Difference = ProposedDifference();

        ScanLaunch launch = await Runner.LaunchAsync(ScanScope.Of(TuneSystem.IsdbT), CancellationToken.None);

        await Eventually.Happens(
            () => Runner.TryPeekProposal(launch.Started!, out _),
            "the proposal is held");

        Assert.True(Runner.TryPeekProposal(launch.Started!, out ScanProposal? proposal));
        Assert.Equal([TuneSystem.IsdbT], proposal.Systems);
    }

    private static ScanDifference ProposedDifference()
        => new(
            [
                new ScanServiceChange(
                    ScanChangeKind.Added,
                    new NetworkId(1),
                    new ServiceId(101),
                    "Arrived",
                    ServiceCategory.Television,
                    [
                        new ScanChannelChange(
                            ScanChangeKind.Added,
                            TuningParameters.Terrestrial(53),
                            null,
                            null),
                    ],
                    Seen: true),
            ],
            []);
}
