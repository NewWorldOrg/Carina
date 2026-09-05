using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

namespace Carina.Infrastructure.Tests.Reservations;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationOutcomeListingTests(RepositoryDatabase database)
{
    private const int Network = 32736;

    private static readonly DateTime Now = ReservationFixtures.Now;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ThePageSaysHowManyThereAreAndHandsBackTheNewestFirst()
    {
        const int Service = 1301;
        await WrittenAsync(
            Outcome(Service, ReservationOutcomeKind.Missed, Now.AddHours(1)),
            Outcome(Service, ReservationOutcomeKind.Missed, Now.AddHours(3)),
            Outcome(Service, ReservationOutcomeKind.Missed, Now.AddHours(2)));

        PaginatedList<ReservationOutcome> page = await ListAsync(Only(Service), perPage: 2);

        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.LastPage);
        Assert.Equal([Now.AddHours(3), Now.AddHours(2)], page.Items.Select(item => item.OccurredAt));
    }

    [Fact]
    public async Task TheSecondPageCarriesOnWhereTheFirstStopped()
    {
        const int Service = 1302;
        await WrittenAsync(
            Outcome(Service, ReservationOutcomeKind.Missed, Now.AddHours(1)),
            Outcome(Service, ReservationOutcomeKind.Missed, Now.AddHours(2)),
            Outcome(Service, ReservationOutcomeKind.Missed, Now.AddHours(3)));

        PaginatedList<ReservationOutcome> first = await ListAsync(Only(Service), perPage: 2);
        PaginatedList<ReservationOutcome> second = await ListAsync(Only(Service), perPage: 2, page: 2);

        Assert.Equal(Now.AddHours(1), Assert.Single(second.Items).OccurredAt);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
    }

    [Fact]
    public async Task OnlyTheKindsAskedForComeBack()
    {
        const int Service = 1303;
        await WrittenAsync(
            Outcome(Service, ReservationOutcomeKind.Competing, Now, recordedInstead: [Guid.NewGuid()]),
            Outcome(Service, ReservationOutcomeKind.Missed, Now.AddMinutes(1)),
            Outcome(Service, ReservationOutcomeKind.TuneFailure, Now.AddMinutes(2), tuneFailure: TuneFailureKind.NoLock),
            Outcome(Service, ReservationOutcomeKind.RecordingFailure, Now.AddMinutes(3), recordingOutcome: RecordingOutcome.Failed));

        PaginatedList<ReservationOutcome> one = await ListAsync(
            Only(Service) with { Kinds = [ReservationOutcomeKind.TuneFailure] });
        PaginatedList<ReservationOutcome> two = await ListAsync(
            Only(Service) with { Kinds = [ReservationOutcomeKind.Competing, ReservationOutcomeKind.Missed] });

        Assert.Equal(ReservationOutcomeKind.TuneFailure, Assert.Single(one.Items).Kind);
        Assert.Equal(
            [ReservationOutcomeKind.Competing, ReservationOutcomeKind.Missed],
            two.Items.Select(item => item.Kind).Order());
    }

    [Fact]
    public async Task OnlyTheChannelsAskedForComeBack()
    {
        const int Wanted = 1304;
        const int Beside = 1305;
        await WrittenAsync(
            Outcome(Wanted, ReservationOutcomeKind.Missed, Now),
            Outcome(Beside, ReservationOutcomeKind.Missed, Now));

        PaginatedList<ReservationOutcome> page = await ListAsync(Only(Wanted));

        Assert.Equal(Wanted, Assert.Single(page.Items).ServiceId.Value);
    }

    [Fact]
    public async Task MoreThanOneChannelIsAskedForAtOnce()
    {
        const int First = 1306;
        const int Second = 1307;
        const int Beside = 1308;
        await WrittenAsync(
            Outcome(First, ReservationOutcomeKind.Missed, Now),
            Outcome(Second, ReservationOutcomeKind.Missed, Now),
            Outcome(Beside, ReservationOutcomeKind.Missed, Now));

        PaginatedList<ReservationOutcome> page = await ListAsync(new ReservationOutcomeConditions
        {
            Channels = [new ProgrammeService(Network, First), new ProgrammeService(Network, Second)],
        });

        Assert.Equal([First, Second], page.Items.Select(item => item.ServiceId.Value).Order());
    }

    [Fact]
    public async Task OnlyWhatCameOfTheRuleAskedForComesBack()
    {
        const int Service = 1309;
        RuleId wanted = RuleId.New();
        await WrittenAsync(
            Outcome(Service, ReservationOutcomeKind.Missed, Now, rule: wanted),
            Outcome(Service, ReservationOutcomeKind.Missed, Now.AddMinutes(1), rule: RuleId.New()),
            Outcome(Service, ReservationOutcomeKind.Missed, Now.AddMinutes(2)));

        PaginatedList<ReservationOutcome> page = await ListAsync(Only(Service) with { Rule = wanted });

        Assert.Equal(wanted, Assert.Single(page.Items).RuleId);
    }

    [Fact]
    public async Task ASpanReadsWhenTheOutcomeWasWrittenDownAndTheStartIsInsideItWhereTheEndIsNot()
    {
        const int Service = 1310;
        await WrittenAsync(
            Outcome(Service, ReservationOutcomeKind.Missed, Now.AddHours(1)),
            Outcome(Service, ReservationOutcomeKind.Missed, Now.AddHours(4)),
            Outcome(Service, ReservationOutcomeKind.Missed, Now.AddHours(8)));

        PaginatedList<ReservationOutcome> page = await ListAsync(
            Only(Service),
            from: Now.AddHours(4),
            to: Now.AddHours(8));

        Assert.Equal(Now.AddHours(4), Assert.Single(page.Items).OccurredAt);
    }

    [Fact]
    public async Task WhatTheLedgerHoldsComesBackAsItWasWrittenDown_BR_RD_012()
    {
        const int Service = 1311;
        Guid[] instead = [Guid.NewGuid(), Guid.NewGuid()];
        ReservationOutcome lost = Outcome(
            Service,
            ReservationOutcomeKind.Competing,
            Now,
            recordedInstead: instead,
            rule: RuleId.New());
        ReservationOutcome failed = Outcome(
            Service,
            ReservationOutcomeKind.RecordingFailure,
            Now.AddMinutes(1),
            recordingOutcome: RecordingOutcome.Failed);
        await WrittenAsync(lost, failed);

        PaginatedList<ReservationOutcome> page = await ListAsync(Only(Service));

        ReservationOutcome readLost = Assert.Single(page.Items, item => item.Id.Equals(lost.Id));
        ReservationOutcome readFailed = Assert.Single(page.Items, item => item.Id.Equals(failed.Id));

        Assert.Equal(instead, readLost.RecordedInstead);
        Assert.Equal(lost.RuleId, readLost.RuleId);
        Assert.Equal(lost.EffectiveStartAt, readLost.EffectiveStartAt);
        Assert.Equal(lost.EffectiveEndAt, readLost.EffectiveEndAt);
        Assert.Equal(lost.SnapshotName, readLost.SnapshotName);
        Assert.Null(readLost.TuneFailure);
        Assert.Null(readLost.RecordingOutcome);
        Assert.Equal(RecordingOutcome.Failed, readFailed.RecordingOutcome);
        Assert.Null(readFailed.TuneFailure);
        Assert.Empty(readFailed.RecordedInstead);
    }

    private static ReservationOutcomeConditions Only(int serviceId)
        => new() { Channels = [new ProgrammeService(Network, serviceId)] };

    private static ReservationOutcome Outcome(
        int serviceId,
        ReservationOutcomeKind kind,
        DateTime occurredAt,
        TuneFailureKind? tuneFailure = null,
        RecordingOutcome? recordingOutcome = null,
        IReadOnlyList<Guid>? recordedInstead = null,
        RuleId? rule = null)
    {
        Reservation reservation = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), serviceId, Now.AddHours(-2)),
            ruleId: rule,
            marginBefore: Margin.OfSeconds(10),
            marginAfter: Margin.OfSeconds(30));

        return ReservationOutcome.Record(
            ReservationOutcomeId.New(),
            reservation,
            kind,
            tuneFailure,
            recordingOutcome,
            recordedInstead ?? [],
            occurredAt);
    }

    private async Task<PaginatedList<ReservationOutcome>> ListAsync(
        ReservationOutcomeConditions conditions,
        DateTime? from = null,
        DateTime? to = null,
        int? page = null,
        int? perPage = null)
    {
        ReservationOutcomeQuery query = ReservationOutcomeQuery.For(from, to, page, perPage, conditions)!;

        await using CarinaDbContext context = database.Open();

        return await new ReservationOutcomeRepository(context).ListAsync(query, Cancel);
    }

    private async Task WrittenAsync(params ReservationOutcome[] outcomes)
    {
        await using CarinaDbContext context = database.Open();
        var repository = new ReservationOutcomeRepository(context);

        foreach (ReservationOutcome outcome in outcomes)
        {
            await repository.AddAsync(outcome, Cancel);
        }
    }
}
