using System.Net;
using System.Text.Json;

using Carina.Domain.Reservations;
using Carina.Domain.Rules;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Rules;

[Trait("Category", "Feature")]
public sealed class DeleteRuleActionTests
{
    [Fact]
    public async Task DeletingARuleTakesTheReservationsItMadeWithIt()
    {
        await using var feature = new RuleFeature();
        feature.Collected();
        Rule written = feature.Written();
        feature.Booked(feature.Announced(1), written.Id);
        Reservation byHand = feature.Booked(feature.Announced(2, "river fishing"));

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/rules/{written.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("withdrawn").GetInt32());
        Assert.Equal(0, body.GetProperty("data").GetProperty("swept").GetInt32());
        Assert.Equal([AppEventName.Reservations, AppEventName.Rules], feature.Events.Signalled);
        Assert.Empty(feature.Rules.Rules);
        Assert.Equal([byHand.Id], [.. feature.Reservations.Held.Select(held => held.Id)]);
    }

    [Fact]
    public async Task DeletingARuleNoCollectionVouchesForStillTakesItsReservationsWithIt()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written();
        feature.Booked(feature.Announced(1), written.Id);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/rules/{written.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, body.GetProperty("data").GetProperty("withdrawn").GetInt32());
        Assert.Equal(1, body.GetProperty("data").GetProperty("swept").GetInt32());
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact]
    public async Task DeletingARuleThatIsNotThereIsAnsweredAsNotThere()
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, _) = await feature.DeleteAsync($"/api/rules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }
}
