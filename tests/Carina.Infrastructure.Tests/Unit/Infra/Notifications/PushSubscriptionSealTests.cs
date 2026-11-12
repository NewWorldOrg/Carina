using System.Text;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Tests.Auth;
using Carina.Infrastructure.Tests.Notifications;
using Carina.TestSupport;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.Unit.Infra.Notifications;

[Trait("Category", "Unit")]
public sealed class PushSubscriptionSealTests : IDisposable
{
    private const string Endpoint = "https://fcm.googleapis.com/fcm/send/made-up-registration";

    private readonly PushReceiver receiver = new();

    [Fact(DisplayName = "BR-NA-003: a sealed subscription opens with the keys that sealed it, the endpoint and both keys as they were")]
    public void ASealedSubscriptionOpensWithTheKeysThatSealedIt()
    {
        PushSubscriptionSeal seal = Seal(SealingKeys.Fresh());
        PushSubscription subscription = Subscription();

        PushSubscription? opened = seal.Open(seal.Seal(subscription));

        Assert.NotNull(opened);
        Assert.Equal(Endpoint, opened.EndpointAsWritten);
        Assert.Equal(receiver.KeyAsWritten, opened.KeyAsWritten);
        Assert.Equal(receiver.SecretAsWritten, opened.SecretAsWritten);
        Assert.Equal(subscription.Fingerprint, opened.Fingerprint);
    }

    [Fact(DisplayName = "BR-NA-003: the sealed form carries neither the endpoint nor either key")]
    public void TheSealedFormCarriesNeitherTheEndpointNorEitherKey()
    {
        byte[] held = Seal(SealingKeys.Fresh()).Seal(Subscription());

        string asText = Encoding.Latin1.GetString(held);
        Assert.DoesNotContain("made-up-registration", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("fcm.googleapis.com", asText, StringComparison.Ordinal);
        Assert.DoesNotContain(receiver.KeyAsWritten, asText, StringComparison.Ordinal);
        Assert.DoesNotContain(receiver.SecretAsWritten, asText, StringComparison.Ordinal);
        Assert.InRange(held.Length, 1, NotificationDestination.LargestSealedUrl);
    }

    [Fact(DisplayName = "BR-NA-003: the longest endpoint still fits the column when sealed with its keys")]
    public void TheLongestEndpointStillFitsTheColumnWhenSealed()
    {
        string longest = Endpoint + new string('a', PushSubscription.LongestEndpoint - Endpoint.Length);

        byte[] held = Seal(SealingKeys.Fresh()).Seal(PushSubscription.Read(longest, receiver.KeyAsWritten, receiver.SecretAsWritten)!);

        Assert.InRange(held.Length, 1, NotificationDestination.LargestSealedUrl);
    }

    [Fact(DisplayName = "BR-NA-003: keys kept in their directory open what was sealed before a restart")]
    public void KeysKeptInTheirDirectoryOpenWhatWasSealedBeforeARestart()
    {
        DirectoryInfo keys = SealingKeys.Fresh();
        byte[] held = Seal(keys).Seal(Subscription());

        Assert.Equal(Endpoint, Seal(keys).Open(held)?.EndpointAsWritten);
    }

    [Fact(DisplayName = "BR-ND-012: a subscription sealed with keys that are gone does not open")]
    public void ASubscriptionSealedWithKeysThatAreGoneDoesNotOpen()
    {
        byte[] held = Seal(SealingKeys.Fresh()).Seal(Subscription());

        Assert.Null(Seal(SealingKeys.Fresh()).Open(held));
    }

    [Fact(DisplayName = "BR-ND-012: bytes that were never sealed do not open")]
    public void BytesThatWereNeverSealedDoNotOpen()
    {
        Assert.Null(Seal(SealingKeys.Fresh()).Open(Encoding.UTF8.GetBytes(Endpoint)));
    }

    [Theory(DisplayName = "BR-ND-012: what was sealed under the same purpose but is not an endpoint and two keys does not open")]
    [InlineData("")]
    [InlineData("https://fcm.googleapis.com/fcm/send/made-up-registration")]
    [InlineData("https://fcm.googleapis.com/fcm/send/made-up-registration\nAAAA")]
    [InlineData("https://fcm.googleapis.com/fcm/send/made-up-registration\nAAAA\nAAAA")]
    [InlineData("one\ntwo\nthree\nfour")]
    public void WhatIsNotASubscriptionDoesNotOpen(string written)
    {
        IDataProtectionProvider provider = Provider(SealingKeys.Fresh());
        byte[] held = provider.CreateProtector(PushSubscriptionSeal.Purpose).Protect(Encoding.UTF8.GetBytes(written));

        Assert.Null(new PushSubscriptionSeal(provider).Open(held));
    }

    [Fact(DisplayName = "BR-NA-003: what is sealed as a webhook address does not open as a subscription, nor the other way round")]
    public void TheSealOfASubscriptionIsItsOwn()
    {
        IDataProtectionProvider provider = Provider(SealingKeys.Fresh());
        WebhookUrlSeal webhooks = new(provider);
        PushSubscriptionSeal subscriptions = new(provider);

        Assert.Equal("Carina.Notifications.PushSubscription", PushSubscriptionSeal.Purpose);
        Assert.Null(subscriptions.Open(webhooks.Seal(WebhookRig.Slack)));
        Assert.Null(webhooks.Open(subscriptions.Seal(Subscription())));
    }

    public void Dispose() => receiver.Dispose();

    private PushSubscription Subscription() => PushSubscription.Read(Endpoint, receiver.KeyAsWritten, receiver.SecretAsWritten)!;

    private static PushSubscriptionSeal Seal(DirectoryInfo keys) => new(Provider(keys));

    private static IDataProtectionProvider Provider(DirectoryInfo keys)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName(SealingKeyOptions.ApplicationName)
            .PersistKeysToFileSystem(keys);

        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }
}
