using System.Net;
using System.Text.Json;

using Carina.Domain.Notifications;

namespace Carina.Api.Tests.FeatureTest;

public sealed class NotificationDeliveryEndpointTests
{
    [Fact(DisplayName = "FR: The latest deliveries of one destination are answered newest first with when, what, the state and the word for the failure")]
    public async Task TheLatestDeliveriesOfOneDestinationAreAnsweredNewestFirst()
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        Guid study = await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        NoticeOnItsWay sent = feature.Waiting(livingRoom, NoticeKind.RecordingCompleted, NotificationFeature.Noon.AddMinutes(-20));
        sent.Delivery.Deliver(NotificationFeature.Noon.AddMinutes(-19));
        NoticeOnItsWay failed = feature.Waiting(livingRoom, NoticeKind.EncodeFailed, NotificationFeature.Noon.AddMinutes(-10));
        failed.Delivery.Fail(NoticeFailure.ServerFailed, NotificationFeature.Noon.AddMinutes(-9));
        feature.Waiting(study, NoticeKind.RecordingFailed, NotificationFeature.Noon.AddMinutes(-5));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(Of(livingRoom));

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement data = body.GetProperty("data");
        Assert.Equal(50, data.GetProperty("perPage").GetInt32());
        JsonElement[] items = [.. data.GetProperty("items").EnumerateArray()];
        Assert.Equal(2, items.Length);
        Assert.Equal(failed.Delivery.Id.Value, items[0].GetProperty("id").GetGuid());
        Assert.Equal("encodeFailed", items[0].GetProperty("kind").GetString());
        Assert.Equal("録画に失敗しました", items[0].GetProperty("title").GetString());
        Assert.Equal(NotificationFeature.Noon.AddMinutes(-10), items[0].GetProperty("occurredAt").GetDateTime().ToUniversalTime());
        Assert.Equal("failed", items[0].GetProperty("state").GetString());
        Assert.Equal(1, items[0].GetProperty("attempts").GetInt32());
        Assert.Equal("serverFailed", items[0].GetProperty("failure").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("sentAt").ValueKind);
        Assert.Equal(NotificationFeature.Noon.AddMinutes(-9).AddSeconds(30), items[0].GetProperty("nextAttemptAt").GetDateTime().ToUniversalTime());
        Assert.Equal(sent.Delivery.Id.Value, items[1].GetProperty("id").GetGuid());
        Assert.Equal("sent", items[1].GetProperty("state").GetString());
        Assert.Equal(NotificationFeature.Noon.AddMinutes(-19), items[1].GetProperty("sentAt").GetDateTime().ToUniversalTime());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("failure").ValueKind);
    }

    [Theory(DisplayName = "FR: At most fifty deliveries of a destination are answered, and a page size above fifty is cut down to it and answered as the size that was used")]
    [InlineData("", 50, 50)]
    [InlineData("&perPage=3", 3, 3)]
    [InlineData("&perPage=200", 50, 50)]
    [InlineData("&perPage=0", 50, 50)]
    public async Task AtMostFiftyDeliveriesAreAnswered(string asked, int size, int answered)
    {
        await using NotificationFeature feature = new();
        Guid destination = await feature.RegisteredAsync();

        foreach (int minute in Enumerable.Range(1, 60))
        {
            feature.Waiting(destination, NoticeKind.RecordingFailed, NotificationFeature.Noon.AddMinutes(-minute));
        }

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(Of(destination) + asked);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(size, body.GetProperty("data").GetProperty("perPage").GetInt32());
        Assert.Equal(answered, body.GetProperty("data").GetProperty("items").GetArrayLength());
    }

    [Theory(DisplayName = "FR: The deliveries are asked for by the destination they went to, and a request that names none is refused")]
    [InlineData("")]
    [InlineData("?destinationId=00000000-0000-0000-0000-000000000000")]
    [InlineData("?destinationId=not-an-id")]
    public async Task DeliveriesAskedForWithoutADestinationAreRefused(string asked)
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(NotificationFeature.Deliveries + asked);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(body.GetProperty("status").GetBoolean());
    }

    [Fact(DisplayName = "FR: The deliveries of a destination that is not held are not found")]
    public async Task TheDeliveriesOfAnUnknownDestinationAreNotFound()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();

        (HttpStatusCode status, JsonElement _) = await feature.GetAsync(Of(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact(DisplayName = "BR-NA-003: The record of deliveries carries neither the address of the destination nor anything the other side answered")]
    public async Task TheRecordOfDeliveriesCarriesNoAddress()
    {
        await using NotificationFeature feature = new();
        Guid destination = await feature.RegisteredAsync();
        NoticeOnItsWay refused = feature.Waiting(destination);
        refused.Delivery.GiveUp(NoticeFailure.Rejected);

        using HttpResponseMessage response = await feature.Client.GetAsync(new Uri(Of(destination), UriKind.Relative));
        string text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"rejected\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain(NotificationFeature.Secret, text, StringComparison.Ordinal);
        Assert.DoesNotContain("hooks.slack.com", text, StringComparison.Ordinal);
        Assert.DoesNotContain("url", text, StringComparison.OrdinalIgnoreCase);
    }

    private static string Of(Guid destination) => $"{NotificationFeature.Deliveries}?destinationId={destination}";
}
