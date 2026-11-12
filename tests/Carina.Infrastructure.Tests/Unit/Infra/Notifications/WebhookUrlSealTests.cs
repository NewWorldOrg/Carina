using System.Text;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Auth;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Tests.Auth;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.Unit.Infra.Notifications;

[Trait("Category", "Unit")]
public sealed class WebhookUrlSealTests
{
    private const string Written = "https://hooks.slack.com/services/T0000/B0000/made-up-token-wxyz";

    private static readonly WebhookUrl Webhook = WebhookUrl.Read(Written)!;

    [Fact(DisplayName = "BR-NA-003: a sealed address opens with the keys that sealed it")]
    public void ASealedAddressOpensWithTheKeysThatSealedIt()
    {
        WebhookUrlSeal seal = Seal(SealingKeys.Fresh());

        WebhookUrl? opened = seal.Open(seal.Seal(Webhook));

        Assert.NotNull(opened);
        Assert.Equal(Written, opened.AddressAsWritten);
        Assert.Equal(WebhookTarget.Slack, opened.Target);
    }

    [Fact(DisplayName = "BR-NA-003: the sealed form does not carry the address, nor any part of its token")]
    public void TheSealedFormDoesNotCarryTheAddress()
    {
        byte[] held = Seal(SealingKeys.Fresh()).Seal(Webhook);

        string asText = Encoding.Latin1.GetString(held);
        Assert.DoesNotContain("made-up-token", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("hooks.slack.com", asText, StringComparison.Ordinal);
        Assert.InRange(held.Length, 1, NotificationDestination.LargestSealedUrl);
    }

    [Fact(DisplayName = "BR-NA-003: the longest address still fits the column when sealed")]
    public void TheLongestAddressStillFitsTheColumnWhenSealed()
    {
        string longest = Written + new string('a', WebhookUrl.Longest - Written.Length);

        byte[] held = Seal(SealingKeys.Fresh()).Seal(WebhookUrl.Read(longest)!);

        Assert.InRange(held.Length, 1, NotificationDestination.LargestSealedUrl);
    }

    [Fact(DisplayName = "BR-NA-003: keys kept in their directory open what was sealed before a restart")]
    public void KeysKeptInTheirDirectoryOpenWhatWasSealedBeforeARestart()
    {
        DirectoryInfo keys = SealingKeys.Fresh();
        byte[] held = Seal(keys).Seal(Webhook);

        Assert.Equal(Written, Seal(keys).Open(held)?.AddressAsWritten);
    }

    [Fact(DisplayName = "BR-ND-012: an address sealed with keys that are gone does not open")]
    public void AnAddressSealedWithKeysThatAreGoneDoesNotOpen()
    {
        byte[] held = Seal(SealingKeys.Fresh()).Seal(Webhook);

        Assert.Null(Seal(SealingKeys.Fresh()).Open(held));
    }

    [Fact(DisplayName = "BR-ND-012: bytes that were never sealed do not open")]
    public void BytesThatWereNeverSealedDoNotOpen()
    {
        Assert.Null(Seal(SealingKeys.Fresh()).Open(Encoding.UTF8.GetBytes(Written)));
    }

    [Fact(DisplayName = "BR-NA-003: what is sealed as a client secret does not open as an address, nor the other way round")]
    public void TheSealOfAnAddressIsItsOwn()
    {
        DirectoryInfo keys = SealingKeys.Fresh();
        IDataProtectionProvider provider = Provider(keys);
        byte[] asASecret = provider.CreateProtector(ClientSecretSeal.Purpose).Protect(Encoding.UTF8.GetBytes(Written));

        Assert.Equal("Carina.Notifications.WebhookUrl", WebhookUrlSeal.Purpose);
        Assert.Null(new WebhookUrlSeal(provider).Open(asASecret));
    }

    private static WebhookUrlSeal Seal(DirectoryInfo keys) => new(Provider(keys));

    private static IDataProtectionProvider Provider(DirectoryInfo keys)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName(SealingKeyOptions.ApplicationName)
            .PersistKeysToFileSystem(keys);

        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }
}
