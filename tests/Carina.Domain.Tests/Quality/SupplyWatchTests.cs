using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class SupplyWatchTests
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    private static readonly QualitySubject Adapter = QualitySubject.Of(QualitySubjectKind.Tuner, "adapter0");

    [Theory(DisplayName = "what a pass does about one supply follows whether it read it, whether it is quiet and whether one already stands")]
    [InlineData(true, true, false, SupplyWatchStep.Open)]
    [InlineData(true, true, true, SupplyWatchStep.Nothing)]
    [InlineData(true, false, true, SupplyWatchStep.Resolve)]
    [InlineData(true, false, false, SupplyWatchStep.Nothing)]
    [InlineData(false, false, true, SupplyWatchStep.Nothing)]
    [InlineData(false, false, false, SupplyWatchStep.Nothing)]
    [InlineData(false, true, false, SupplyWatchStep.Nothing)]
    [InlineData(false, true, true, SupplyWatchStep.Nothing)]
    public void WhatAPassDoesAboutOneSupplyFollowsWhetherItReadItAndWhetherItIsQuiet(
        bool observed,
        bool quiet,
        bool standing,
        SupplyWatchStep expected)
        => Assert.Equal(expected, SupplyWatch.NextStep(observed, quiet, standing));

    [Fact(DisplayName = "every supply is answered for by the driver or by the ledger, and by only one of them")]
    public void EverySupplyIsAnsweredForByTheDriverOrByTheLedgerAndByOnlyOneOfThem()
    {
        Assert.Equal(
            SupplySilences.Every.Order(),
            SupplySilences.TheDriverAnswersFor.Concat(SupplySilences.TheLedgerAnswersFor).Order());
        Assert.Empty(SupplySilences.TheDriverAnswersFor.Intersect(SupplySilences.TheLedgerAnswersFor));
        Assert.Contains(SupplySilence.SignalSamples, SupplySilences.TheDriverAnswersFor);
    }

    [Fact(DisplayName = "a supply this pass could not read is neither opened nor resolved")]
    public void ASupplyThisPassCouldNotReadIsNeitherOpenedNorResolved()
    {
        QualityIncident standing = Standing();

        SupplyWatchPlan plan = SupplyWatch.Plan([], [standing], SupplySilences.TheLedgerAnswersFor);

        Assert.Empty(plan.ToOpen);
        Assert.Empty(plan.ToResolve);
        Assert.Equal(QualityIncidentState.Detected, standing.State);
    }

    [Fact(DisplayName = "a supply heard from within the threshold is not quiet")]
    public void ASupplyHeardFromWithinTheThresholdIsNotQuiet()
        => Assert.Empty(SupplyWatch.Quiet(
            [SupplyReading.Of(SupplySilence.SignalSamples, Adapter, Noon - FiveMinutes + TimeSpan.FromSeconds(1))],
            FiveMinutes,
            Noon));

    [Fact(DisplayName = "a supply last heard from a whole threshold ago is quiet")]
    public void ASupplyLastHeardFromAWholeThresholdAgoIsQuiet()
    {
        SupplySilenceFinding found = Assert.Single(SupplyWatch.Quiet(
            [SupplyReading.Of(SupplySilence.SignalSamples, Adapter, Noon - FiveMinutes)],
            FiveMinutes,
            Noon));

        Assert.Equal(SupplySilence.SignalSamples, found.Silence);
        Assert.Equal(Adapter, found.Subject);
        Assert.Equal(FiveMinutes.TotalSeconds, found.Seconds);
    }

    [Fact(DisplayName = "the four supplies are told apart rather than counted as one silence")]
    public void TheFourSuppliesAreToldApartRatherThanCountedAsOneSilence()
    {
        QualitySubject recording = QualitySubject.Of(QualitySubjectKind.Recording, "a-recording");

        IReadOnlyList<SupplySilenceFinding> quiet = SupplyWatch.Quiet(
            [
                SupplyReading.Of(SupplySilence.RecordingProgress, recording, Noon - FiveMinutes),
                SupplyReading.Of(SupplySilence.RecordingMeasurement, recording, Noon - FiveMinutes),
            ],
            FiveMinutes,
            Noon);

        Assert.Equal(2, quiet.Count);

        SupplyWatchPlan plan = SupplyWatch.Plan(quiet, [], SupplySilences.Every);

        Assert.Equal(
            [SupplySilence.RecordingProgress, SupplySilence.RecordingMeasurement],
            plan.ToOpen.Select(finding => finding.Silence));
    }

    [Fact(DisplayName = "a silence that is already standing is not opened a second time")]
    public void ASilenceThatIsAlreadyStandingIsNotOpenedASecondTime()
    {
        SupplyWatchPlan plan = SupplyWatch.Plan([Found()], [Standing()], SupplySilences.Every);

        Assert.Empty(plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "a supply heard from again resolves the one that stands for it")]
    public void ASupplyHeardFromAgainResolvesTheOneThatStandsForIt()
    {
        QualityIncident standing = Standing();

        SupplyWatchPlan plan = SupplyWatch.Plan([], [standing], SupplySilences.Every);

        Assert.Empty(plan.ToOpen);
        Assert.Same(standing, Assert.Single(plan.ToResolve));
    }

    [Fact(DisplayName = "the same condition after a resolution is a new occurrence")]
    public void TheSameConditionAfterAResolutionIsANewOccurrence()
    {
        QualityIncident settled = Standing();

        settled.Notify(Noon);
        settled.Resolve(Noon);

        SupplyWatchPlan plan = SupplyWatch.Plan([Found()], [settled], SupplySilences.Every);

        Assert.Single(plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "a silence that goes on after it was told about does not open a second one for it")]
    public void ASilenceThatGoesOnAfterItWasToldAboutDoesNotOpenASecondOneForIt()
    {
        QualityIncident told = Standing();

        told.Notify(Noon);

        SupplyWatchPlan plan = SupplyWatch.Plan([Found()], [told], SupplySilences.Every);

        Assert.Empty(plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "an anomaly another domain owns is left where its owner put it")]
    public void AnAnomalyAnotherDomainOwnsIsLeftWhereItsOwnerPutIt()
    {
        QualityIncident elsewhere = QualityIncident.Detect(
            QualityIncidentId.New(),
            Noon,
            QualityThresholdKey.PacketsLostWarning,
            Adapter,
            0.004,
            QualityThresholdShapes.AsShipped(QualityThresholdKey.PacketsLostWarning, Noon),
            QualityIncidentOwner.Tuner,
            "NoLock");

        SupplyWatchPlan plan = SupplyWatch.Plan([], [elsewhere], SupplySilences.Every);

        Assert.Empty(plan.ToResolve);
    }

    [Fact]
    public void ASupplyWatchHoldsNothingAgainstAThresholdOfNothing()
        => Assert.Throws<ArgumentOutOfRangeException>(() => SupplyWatch.Quiet([], TimeSpan.Zero, Noon));

    private static SupplySilenceFinding Found()
        => SupplySilenceFinding.Of(SupplySilence.SignalSamples, Adapter, FiveMinutes);

    private static QualityIncident Standing()
        => QualityIncident.Detect(
            QualityIncidentId.New(),
            Noon,
            QualityThresholdKey.SupplySilence,
            Adapter,
            FiveMinutes.TotalSeconds,
            QualityThresholdShapes.AsShipped(QualityThresholdKey.SupplySilence, Noon),
            silence: SupplySilence.SignalSamples);
}
