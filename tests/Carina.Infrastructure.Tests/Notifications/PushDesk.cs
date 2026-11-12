using Carina.Domain.Notifications;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Notifications;

/// <summary>
/// What sending to a browser needs besides the line: the seal of the subscriptions, the key of the server in a
/// directory of its own that goes when the test is over, and the watches the handling of a message is read from.
/// </summary>
internal sealed class PushDesk : IDisposable
{
    public static readonly Uri Screens = new("https://screens.example");

    public PushDesk(IDataProtectionProvider keys, TimeProvider clock)
    {
        Directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"carina-push-desk-{Guid.NewGuid():N}"));
        Seal = new PushSubscriptionSeal(keys);
        Key = new PushServerKey(Options.Create(new SealingKeyOptions { Directory = Directory.FullName }));
        Messages = new PushMessages(Key, NoticeLinkBase.At(Screens), clock);
        Handling = new PushHandlingDomainService(Watches);
    }

    public DirectoryInfo Directory { get; }

    public PushSubscriptionSeal Seal { get; }

    public PushServerKey Key { get; }

    public PushMessages Messages { get; }

    public HeldProgrammeWatches Watches { get; } = new();

    public PushHandlingDomainService Handling { get; }

    public void Dispose()
    {
        Directory.Refresh();

        if (Directory.Exists)
        {
            Directory.Delete(recursive: true);
        }
    }
}
