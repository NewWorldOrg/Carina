namespace Carina.Domain.Notifications;

/// <summary>
/// What a notice says: a title, a body of one fact a line, and a link to the screen that shows the rest, when the
/// address the screens are reached at is known.
/// </summary>
public sealed record NoticeWords(string Title, string Body, string? Link);
