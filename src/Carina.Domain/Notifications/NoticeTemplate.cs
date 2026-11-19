namespace Carina.Domain.Notifications;

internal sealed record NoticeTemplate<TSource>(string Title, Func<TSource, NoticeLines> Lines, Func<TSource, string> Screen)
    where TSource : NoticeSource;
