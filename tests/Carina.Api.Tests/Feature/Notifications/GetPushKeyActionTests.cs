using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class GetPushKeyActionTests
{
    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("GET", NotificationFeature.PushKey)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A read from another origin is left alone")]
    [InlineData("GET", NotificationFeature.PushKey)]
    public async Task AReadFromAnotherOriginIsLeftAlone(string method, string path)
        => await NotificationGuard.AReadFromAnotherOriginIsLeftAloneAsync(method, path);

    [Fact(DisplayName = "D-11: The key browsers subscribe with is an uncompressed P-256 point in base64url, the same each time, whether or not the public origin is named")]
    public async Task TheKeyBrowsersSubscribeWithIsServed()
    {
        await using NotificationFeature named = new(NotificationFeature.Screens);
        await using NotificationFeature unnamed = new();

        (HttpStatusCode first, JsonElement once) = await named.GetAsync(NotificationFeature.PushKey);
        (HttpStatusCode second, JsonElement again) = await named.GetAsync(NotificationFeature.PushKey);
        (HttpStatusCode third, JsonElement elsewhere) = await unnamed.GetAsync(NotificationFeature.PushKey);

        Assert.Equal(HttpStatusCode.OK, first);
        Assert.Equal(HttpStatusCode.OK, second);
        Assert.Equal(HttpStatusCode.OK, third);
        string written = once.GetProperty("data").GetProperty("publicKey").GetString()!;
        byte[] point = Base64Url.DecodeFromChars(written);
        Assert.Equal(65, point.Length);
        Assert.Equal(0x04, point[0]);
        Assert.DoesNotContain('=', written);
        Assert.Equal(written, again.GetProperty("data").GetProperty("publicKey").GetString());
        Assert.Equal(written, elsewhere.GetProperty("data").GetProperty("publicKey").GetString());
    }

    [Fact(DisplayName = "D-11: Where the key this server signs with cannot be read, it is not served, a trial of a browser is refused without sending, and the file is left as it was")]
    public async Task WhereTheServerKeyCannotBeReadNothingIsSentToABrowser()
    {
        DirectoryInfo kept = Directory.CreateTempSubdirectory("carina-push-key-");
        string file = Path.Combine(kept.FullName, PushServerKey.FileName);
        await File.WriteAllTextAsync(file, "not a key");

        try
        {
            await using NotificationFeature feature = new(
                NotificationFeature.Screens,
                also: services => services.AddSingleton(
                    new PushServerKey(Options.Create(new SealingKeyOptions { Directory = kept.FullName }))));
            using PushReceiver browser = new();
            Guid id = await feature.SubscribedAsync(browser);

            (HttpStatusCode served, JsonElement refusal) = await feature.GetAsync(NotificationFeature.PushKey);
            (HttpStatusCode tried, _) = await feature.PostAsync(NotificationFeature.TrialOf(id));

            Assert.Equal(HttpStatusCode.Conflict, served);
            Assert.False(refusal.GetProperty("status").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict, tried);
            Assert.Empty(feature.End.Heard);
            Assert.Equal("not a key", await File.ReadAllTextAsync(file));
        }
        finally
        {
            kept.Delete(recursive: true);
        }
    }
}
