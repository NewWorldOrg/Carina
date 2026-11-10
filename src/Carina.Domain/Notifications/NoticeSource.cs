namespace Carina.Domain.Notifications;

/// <summary>
/// The kind of a notice and the row it was taken from, which together name it in the ledger.
/// </summary>
public sealed record NoticeKey(NoticeKind Kind, Guid SourceId);

/// <summary>
/// One thing worth telling that a row of another ledger shows to have happened.
/// </summary>
public sealed record NoticeHappening(NoticeKind Kind, Guid SourceId, DateTime OccurredAt, NoticeSource Source)
{
    public NoticeKey Key => new(Kind, SourceId);
}

/// <summary>
/// What a row of another ledger says, read down to what a notice of it may say.
/// </summary>
public abstract record NoticeSource
{
    public abstract IEnumerable<NoticeHappening> Happenings();
}
