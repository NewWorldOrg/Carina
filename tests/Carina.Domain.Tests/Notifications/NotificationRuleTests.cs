using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Notifications;

public sealed class NotificationRuleTests
{
    private static readonly DateTime At = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-NT-004: a set of rules covers the kinds it lists while it is turned on")]
    public void ASetOfRulesCoversTheKindsItListsWhileItIsTurnedOn()
    {
        NotificationRule rule = Rule([NoticeKind.RecordingFailed, NoticeKind.EncodeFailed]);

        Assert.True(rule.Covers(NoticeKind.RecordingFailed));
        Assert.False(rule.Covers(NoticeKind.RecordingCompleted));

        rule.Disable(At.AddMinutes(1));

        Assert.False(rule.Covers(NoticeKind.RecordingFailed));
        Assert.Equal(At.AddMinutes(1), rule.ChangedAt);
    }

    [Fact(DisplayName = "D-9: the set of rules made with the first destination tells it of failures and trouble, and of nothing that went well")]
    public void TheUsualSetOfRulesTellsOfFailuresAndTrouble()
    {
        NotificationDestinationId destination = NotificationDestinationId.New();

        NotificationRule usual = NotificationRule.Usual(NotificationRuleId.New(), destination, At);

        Assert.Equal("失敗と異常", usual.Name.Value);
        Assert.True(usual.Enabled);
        Assert.Equal([destination], usual.Destinations);
        Assert.Equal(
            [
                NoticeKind.RecordingTruncated,
                NoticeKind.RecordingFailed,
                NoticeKind.EncodeFailed,
                NoticeKind.ReservationCompeting,
                NoticeKind.ReservationMissed,
                NoticeKind.ProgrammeMoved,
                NoticeKind.QualityIncidentDetected,
                NoticeKind.QualityIncidentResolved,
                NoticeKind.DeviceDisconnected,
            ],
            usual.Events);
        Assert.Equal(At, usual.CreatedAt);
    }

    [Fact(DisplayName = "BR-NT-004: a set of rules that lists no kind is kept and covers nothing")]
    public void ASetOfRulesThatListsNoKindCoversNothing()
    {
        NotificationRule rule = Rule([]);

        Assert.Empty(rule.Events);
        Assert.DoesNotContain(Enum.GetValues<NoticeKind>(), rule.Covers);
    }

    [Fact(DisplayName = "BR-NT-004: a set of rules lists only kinds the ledger holds, once each")]
    public void ASetOfRulesListsOnlyKnownKindsOnceEach()
    {
        NotificationRule rule = Rule([NoticeKind.EncodeFailed, NoticeKind.RecordingFailed, NoticeKind.EncodeFailed]);

        Assert.Equal([NoticeKind.RecordingFailed, NoticeKind.EncodeFailed], rule.Events);
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule([(NoticeKind)99]));
    }

    [Theory(DisplayName = "D-10: a set of rules lists no kind told of one programme somebody asked to be told of")]
    [InlineData(NoticeKind.ProgrammeStarting)]
    [InlineData(NoticeKind.WatchedProgrammeMoved)]
    [InlineData(NoticeKind.WatchedProgrammeGone)]
    public void ASetOfRulesListsNoKindToldOfOneProgramme(NoticeKind kind)
    {
        Assert.Contains(kind, NoticeKinds.OfAWatchedProgramme);
        Assert.DoesNotContain(kind, NoticeKinds.ChosenByRules);
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule([NoticeKind.RecordingFailed, kind]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Rule([NoticeKind.RecordingFailed]).Tell([kind], [NotificationDestinationId.New()], At));
    }

    [Fact(DisplayName = "D-10: every kind is either chosen by rules or told of one programme, never both")]
    public void EveryKindIsChosenByRulesOrToldOfOneProgramme()
    {
        Assert.Equal(
            Enum.GetValues<NoticeKind>().Order(),
            NoticeKinds.ChosenByRules.Concat(NoticeKinds.OfAWatchedProgramme).Order());
    }

    [Fact(DisplayName = "BR-NT-004: a set of rules sends to at least one destination, each once")]
    public void ASetOfRulesSendsToAtLeastOneDestination()
    {
        NotificationDestinationId destination = NotificationDestinationId.New();

        NotificationRule rule = NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("Failures"),
            [NoticeKind.RecordingFailed],
            [destination, new NotificationDestinationId(destination.Value)],
            At);

        Assert.Equal([destination], rule.Destinations);
        Assert.Throws<ArgumentException>(
            () => NotificationRule.Define(NotificationRuleId.New(), new NotificationName("Failures"), [NoticeKind.RecordingFailed], [], At));
        Assert.Throws<ArgumentException>(() => rule.Tell([NoticeKind.RecordingFailed], [], At));
    }

    [Fact(DisplayName = "a change to what a set of rules tells replaces both lists and says when")]
    public void AChangeReplacesBothLists()
    {
        NotificationRule rule = Rule([NoticeKind.RecordingFailed]);
        NotificationDestinationId another = NotificationDestinationId.New();

        rule.Tell([NoticeKind.EncodeCompleted], [another], At.AddHours(1));
        rule.Rename(new NotificationName("Encodes"), At.AddHours(2));

        Assert.Equal([NoticeKind.EncodeCompleted], rule.Events);
        Assert.Equal([another], rule.Destinations);
        Assert.Equal("Encodes", rule.Name.Value);
        Assert.Equal(At, rule.CreatedAt);
        Assert.Equal(At.AddHours(2), rule.ChangedAt);
    }

    [Fact(DisplayName = "BR-NT-004: a destination taken out of a set of rules leaves the others it sends to")]
    public void ADestinationTakenOutLeavesTheOthers()
    {
        NotificationDestinationId first = NotificationDestinationId.New();
        NotificationDestinationId second = NotificationDestinationId.New();
        NotificationRule rule = NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("Failures"),
            [NoticeKind.RecordingFailed],
            [first, second],
            At);

        Assert.Equal(NotificationRuleNarrowing.Narrowed, rule.StopSendingTo(first, At.AddHours(1)));
        Assert.Equal([second], rule.Destinations);
        Assert.Equal([NoticeKind.RecordingFailed], rule.Events);
        Assert.Equal(At.AddHours(1), rule.ChangedAt);
    }

    [Fact(DisplayName = "BR-NT-004: a set of rules left with no destination is to be removed, and one that does not name it is left alone")]
    public void ASetOfRulesLeftWithNoDestinationIsToBeRemoved()
    {
        NotificationRule rule = Rule([NoticeKind.RecordingFailed]);
        NotificationDestinationId only = rule.Destinations[0];

        Assert.Equal(NotificationRuleNarrowing.NotNamed, rule.StopSendingTo(NotificationDestinationId.New(), At.AddHours(1)));
        Assert.Equal(NotificationRuleNarrowing.LeftWithNone, rule.StopSendingTo(only, At.AddHours(2)));
        Assert.Equal([only], rule.Destinations);
        Assert.Equal(At, rule.ChangedAt);
    }

    private static NotificationRule Rule(IReadOnlyList<NoticeKind> events)
        => NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("Failures"),
            events,
            [NotificationDestinationId.New()],
            At);
}
