using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class ListNotificationWatchesActionTests
{
    private static readonly DateTime Starts = NotificationFeature.Noon.AddHours(2);

    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("GET", NotificationFeature.Watches)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A read from another origin is left alone")]
    [InlineData("GET", NotificationFeature.Watches)]
    public async Task AReadFromAnotherOriginIsLeftAlone(string method, string path)
        => await NotificationGuard.AReadFromAnotherOriginIsLeftAloneAsync(method, path);

    [Fact(DisplayName = "BR-NS-003: The programmes asked to be told of are listed with their states, the earliest start first")]
    public async Task TheProgrammesAskedAreListedTheEarliestFirst()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        Guid later = await feature.AskedAsync(feature.Announced(4002, Starts.AddHours(3), "作り物の番組 その二"), Starts.AddHours(3));
        Guid sooner = await feature.AskedAsync(feature.Announced(4001, Starts), Starts);
        Guid withdrawn = await feature.AskedAsync(feature.Announced(4003, Starts.AddHours(1), "作り物の番組 その三"), Starts.AddHours(1));
        await feature.DeleteAsync(NotificationFeature.WatchOf(withdrawn));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(NotificationFeature.Watches);

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement[] items = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];
        Assert.Equal([sooner, withdrawn, later], items.Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(["waiting", "cancelled", "waiting"], items.Select(item => item.GetProperty("state").GetString()));
        Assert.Equal("作り物の番組 その二", items[2].GetProperty("programmeName").GetString());
    }
}
