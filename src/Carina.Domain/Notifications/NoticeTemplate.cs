namespace Carina.Domain.Notifications;

/// <summary>
/// How one kind of notice is worded from the row it is taken from: its title, its lines, and the screen it links to.
/// </summary>
internal sealed record NoticeTemplate<TSource>(string Title, Func<TSource, NoticeLines> Lines, Func<TSource, string> Screen)
    where TSource : NoticeSource;
