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

        Assert.Equal("失敗と異常を全部の送り先へ", usual.Name.Value);
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

    private static NotificationRule Rule(IReadOnlyList<NoticeKind> events)
        => NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("Failures"),
            events,
            [NotificationDestinationId.New()],
            At);
}
