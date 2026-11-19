namespace Carina.Domain.Notifications;

public sealed record NoticeKey(NoticeKind Kind, Guid SourceId);

public sealed record NoticeHappening(NoticeKind Kind, Guid SourceId, DateTime OccurredAt, NoticeSource Source)
{
    public NoticeKey Key => new(Kind, SourceId);
}

public abstract record NoticeSource
{
    public abstract IEnumerable<NoticeHappening> Happenings();
}
