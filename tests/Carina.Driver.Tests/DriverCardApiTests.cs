using System.Net;

using Carina.Contracts;
using Carina.Driver.Descrambling;
using Carina.Driver.Ipc;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Driver.Tests;

public sealed class DriverCardApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    [Fact]
    public async Task TheCardInHandIsNamedByItsCaSystemAndItsCardId()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection _);
        Descramblers cards = Descramblers.Over(() => service, logger: null);
        cards.Open(HeadHold.EveryProgramme)?.Dispose();

        await using DriverUnderTest driver = await DriverUnderTest.Start(
            reshapeServices: services => services.AddSingleton<IDescramblerFactory>(cards)
        );
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.ConditionalAccessCard, Soon());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        ConditionalAccessCardDto? card = await DriverUnderTest.Read(
            response,
            DriverJson.Context.ConditionalAccessCardDto
        );

        Assert.NotNull(card);
        Assert.Equal(SyntheticCardKeys.CaSystemId, card.CaSystemId);
        Assert.Equal("0A0B0C0D0E0F", card.CardId);
    }

    [Fact]
    public async Task ADriverNoCardHasAnsweredSaysSo()
    {
        FakeSmartCardService service = new();
        Descramblers cards = Descramblers.Over(() => service, logger: null);

        await using DriverUnderTest driver = await DriverUnderTest.Start(
            reshapeServices: services => services.AddSingleton<IDescramblerFactory>(cards)
        );
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.ConditionalAccessCard, Soon());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem);

        Assert.Equal(DriverApi.NoCardTitle, problem?.Title);
    }

    [Fact]
    public async Task ADriverThatAsksNoCardHasNoneToName()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.ConditionalAccessCard, Soon());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void TheGreetingDeclaresThatTheCardCanBeNamed()
    {
        Assert.Contains(DriverCapabilities.CardIdentification, DriverGreeting.Capabilities);
    }
}
