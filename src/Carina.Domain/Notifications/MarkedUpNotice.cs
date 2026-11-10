namespace Carina.Domain.Notifications;

/// <summary>
/// The title and the body of a notice, escaped and cut for one kind of destination.
/// </summary>
public sealed record MarkedUpNotice(string Title, string Body);
