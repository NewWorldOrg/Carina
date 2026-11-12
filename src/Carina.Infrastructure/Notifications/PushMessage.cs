using Carina.Domain.Notifications;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// One message on its way to a push service: what the browser is told, not yet encrypted, the server's proof of who
/// sends it, and how the push service is asked to handle it.
/// </summary>
public sealed record PushMessage(byte[] Content, string Authorization, PushHandling Handling);
