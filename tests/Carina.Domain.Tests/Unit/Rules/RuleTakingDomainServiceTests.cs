using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Domain.Tests.Unit.Reservations;

using static Carina.Domain.Tests.Unit.Reservations.ReservationFactory;

namespace Carina.Domain.Tests.Unit.Rules;

[Trait("Category", "Unit")]
public sealed class RuleTakingDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly HeldReservationLedger reservations = new();

    private RuleTakingDomainService Taking => new(reservations);

    [Fact(DisplayName = "BR-RS-001: a rule reserves a programme no reservation stands for")]
    public async Task ARuleReservesAProgrammeNothingStandsFor()
    {
        RuleProgrammeTaking taken = await Taking.TakingAsync(Programme(4001), Now, Cancel);

        Assert.Equal(new RuleProgrammeTaking(RuleTakingMove.Make, null), taken);
    }

    [Fact(DisplayName = "BR-RS-001: a rule brings back its reservation the guide took out, and leaves any other standing reservation as it is")]
    public async Task ARuleBringsBackWhatTheGuideTookOutAndLeavesTheRest()
    {
        var rule = RuleId.New();
        Reservation gone = Rehydrated(
            ReservationState.Cancelled,
            null,
            null,
            cancellation: ReservationCancellation.ProgrammeGone,
            ruleId: rule);
        reservations.Reservations.Add(gone);

        Assert.Equal(new RuleProgrammeTaking(RuleTakingMove.Revive, gone), await Taking.TakingAsync(gone.Programme, Now, Cancel));
        Assert.Equal(
            new RuleProgrammeTaking(RuleTakingMove.Leave, gone),
            await Taking.TakingAsync(gone.Programme, gone.EffectiveEndAt, Cancel));

        reservations.Reservations.Clear();
        Reservation cancelled = Rehydrated(ReservationState.Cancelled, null, null, ruleId: rule);
        reservations.Reservations.Add(cancelled);

        Assert.Equal(
            new RuleProgrammeTaking(RuleTakingMove.Leave, cancelled),
            await Taking.TakingAsync(cancelled.Programme, Now, Cancel));
    }

    [Fact(DisplayName = "BR-RD-010: the reservations a person changed are those whose priority, margins or wish differ from their standing rule's")]
    public async Task TheReservationsAPersonChangedDifferFromTheirRule()
    {
        Rule rule = Rule.Draft(
            RuleId.New(),
            "A rule",
            new RuleQuery("keyword=news"),
            Priority.Default,
            true,
            Margin.None,
            Margin.None,
            Now);
        Reservation asked = Planned(ruleId: rule.Id, programme: Programme(4001));
        Reservation changed = Planned(ruleId: rule.Id, programme: Programme(4002));
        changed.Reprioritise(new Priority(9));
        Reservation another = Planned(ruleId: RuleId.New(), programme: Programme(4003));
        another.Reprioritise(new Priority(9));
        reservations.Reservations.AddRange([asked, changed, another]);

        Assert.Equal([changed], await Taking.ChangedByHandAsync([rule], Now, Cancel));
    }

    [Fact(DisplayName = "BR-RD-011: a reservation another rule made changes hands when a rule takes its programme")]
    public void AReservationAnotherRuleMadeChangesHands()
    {
        var mine = RuleId.New();

        Assert.True(Planned(ruleId: RuleId.New()).IsHeldByAnotherRuleThan(mine));
        Assert.False(Planned(ruleId: mine).IsHeldByAnotherRuleThan(mine));
        Assert.False(Planned().IsHeldByAnotherRuleThan(mine));
    }
}
