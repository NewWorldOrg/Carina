using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.TestSupport;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.DriverStatus;

[Trait("Category", "Feature")]
public sealed class GetDriverCardReaderActionTests
{
    private static readonly Uri CardReader = new("/api/driver/card-reader", UriKind.Relative);

    [Fact]
    public async Task TheCardReaderIsAnsweredAsTheDriverSawIt()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(HardwareStock.Capable(), driver =>
        {
            HardwareStock.Stocked(driver);
            driver.CardReader = new CardReaderDto
            {
                Use = CardReaderUse.Local,
                State = CardState.Inserted,
                Readers = [new CardReaderSlotDto { Name = "Example Reader", State = CardState.Inserted, InUse = true }],
                LeasesOut = 1,
                LastAnsweredAt = HardwareStock.Moment,
                CheckedAt = HardwareStock.Moment,
            };
        });

        using HttpResponseMessage response = await feature.Client.GetAsync(CardReader);
        (HttpStatusCode status, JsonElement body) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement data = body.GetProperty("data");
        Assert.Equal("local", data.GetProperty("use").GetString());
        Assert.Equal("inserted", data.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("problem").ValueKind);
        Assert.Equal("Example Reader", data.GetProperty("readers")[0].GetProperty("name").GetString());
        Assert.True(data.GetProperty("readers")[0].GetProperty("inUse").GetBoolean());
        Assert.Equal(1, data.GetProperty("leasesOut").GetInt32());
        Assert.Equal(HardwareStock.Moment, data.GetProperty("lastAnsweredAt").GetDateTimeOffset());
        Assert.False(data.TryGetProperty("cardId", out _));
    }

    [Fact]
    public async Task ADriverThatDoesNotAnswerForItsReaderIsNotImplemented()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a", capabilities: [DriverCapabilities.TunerLedger]),
            HardwareStock.Stocked);

        using HttpResponseMessage response = await feature.Client.GetAsync(CardReader);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal(0, feature.Driver.RequestsFor(DriverEndpoints.CardReader));
    }

    [Fact]
    public async Task ADriverThatCannotBeReachedLeavesTheReaderUnavailable()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync();

        using HttpResponseMessage response = await feature.Client.GetAsync(CardReader);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
