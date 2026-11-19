using Carina.Domain.Notifications;

namespace Carina.Infrastructure.Notifications;

public sealed record PushMessage(byte[] Content, string Authorization, PushHandling Handling);
