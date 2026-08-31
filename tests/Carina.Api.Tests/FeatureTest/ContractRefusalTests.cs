using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Carina.Api.Authentication;
using Carina.Api.Tests.Unit;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.FeatureTest;

internal static class ContractAgreement
{
    public static IEnumerable<string> Disagreements(
        JsonNode document,
        string path,
        string method,
        HttpStatusCode status,
        string body)
    {
        JsonObject? answers = document["paths"]?[path]?[method.ToLowerInvariant()]?["responses"]?.AsObject();
        string named = ((int)status).ToString(CultureInfo.InvariantCulture);

        if (answers is null)
        {
            return [$"{method} {path} answered {named} and is not described at all"];
        }

        if (answers[named] is not { } described)
        {
            return [$"{method} {path} answered {named}, which is not among {Described(answers)}"];
        }

        bool describesABody = described["content"] is not null;
        bool carriesABody = body.Length > 0;

        if (describesABody == carriesABody)
        {
            return [];
        }

        return
        [
            describesABody
                ? $"{method} {path} answered {named} with nothing, though a body is described"
                : $"{method} {path} answered {named} carrying a body, though none is described",
        ];
    }

    private static string Described(JsonObject answers)
        => string.Join(", ", answers.Select(answer => answer.Key).Order(StringComparer.Ordinal));
}

[Collection(FeatureTestCollection.Name)]
public sealed class ContractRefusalTests
{
    private const string Password = "/api/auth/password";

    private const string NotTheOneOnTheAccount = "not the one on the account";

    private const string AReplacementLongEnough = "a replacement long enough";

    private const string TooShortToBeWorthHaving = "short";

    private const int TheSurfacesThisRepositoryHadWhenTheSweepWasWritten = 50;

    private static readonly Uri Endpoint = new(Password, UriKind.Relative);

    [Fact]
    public async Task EveryWayThePasswordChangeCanEndIsAnAnswerTheDocumentDescribesAndShapesTheSameWay()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();

        using HttpResponseMessage withoutASession = await Refused(probe);

        await probe.SignedInAsync();

        using HttpResponseMessage wrongCurrentOne = await Asking(probe, NotTheOneOnTheAccount, AReplacementLongEnough);
        using HttpResponseMessage tooWeakAReplacement = await Asking(probe, AuthProbe.Password, TooShortToBeWorthHaving);
        using HttpResponseMessage changed = await Asking(probe, AuthProbe.Password, AReplacementLongEnough);

        JsonNode document = await ServedOpenApi.FetchAsync(probe.Wired);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutASession.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrentOne.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooWeakAReplacement.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEmpty(await Said(wrongCurrentOne));
        Assert.Empty(
            await DisagreementsAsync(
                document,
                withoutASession,
                wrongCurrentOne,
                tooWeakAReplacement,
                changed));
    }

    [Fact]
    public async Task TheOneRefusalStillAnsweringWith401AndABodyIsTheOneOutsideTheGate()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage refused = await probe.LogInAsync(
            FirstCredentials.Username,
            NotTheOneOnTheAccount);

        JsonNode document = await ServedOpenApi.FetchAsync(probe.Wired);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.NotEmpty(await Said(refused));
        Assert.Empty(ContractAgreement.Disagreements(
            document,
            "/api/auth/login",
            HttpMethods.Post,
            refused.StatusCode,
            await refused.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task NoSurfaceAnswersACallerCarryingAGoodSessionWithTheRefusalThatMeansThereIsNone()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        var refused = new List<string>();
        int asked = 0;

        foreach (RoutedSurface surface in RouteInventory.Of(probe.Wired))
        {
            string path = RouteInventory.SamplePath(surface.Pattern);

            if (AnonymousSurfaces.WhileDeveloping.Admit(surface.Method, path))
            {
                continue;
            }

            await probe.SignedInAsync();

            using var asking = new HttpRequestMessage(
                new HttpMethod(surface.Method),
                new Uri(path, UriKind.Relative));

            if (!HttpMethods.IsGet(surface.Method) && !HttpMethods.IsHead(surface.Method))
            {
                asking.Content = AuthProbe.Json();
            }

            using HttpResponseMessage response = await probe.Client.SendAsync(
                asking,
                HttpCompletionOption.ResponseHeadersRead);

            asked++;

            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                refused.Add($"{surface.Method} {path} answered 401 to a caller carrying a session");
            }
        }

        Assert.Empty(refused);
        Assert.True(
            asked >= TheSurfacesThisRepositoryHadWhenTheSweepWasWritten,
            $"the sweep asked {asked} surfaces, which is fewer than it was written against");
    }

    private static async Task<HttpResponseMessage> Refused(AuthProbe probe)
    {
        probe.WithAnAccount();

        return await Asking(probe, AuthProbe.Password, AReplacementLongEnough);
    }

    private static Task<HttpResponseMessage> Asking(AuthProbe probe, string current, string replacement)
        => probe.Client.PostAsJsonAsync(
            Endpoint,
            new { currentPassword = current, newPassword = replacement });

    private static async Task<string> Said(HttpResponseMessage response)
        => JsonNode.Parse(await response.Content.ReadAsStringAsync())!["message"]!.GetValue<string>();

    private static async Task<IReadOnlyList<string>> DisagreementsAsync(
        JsonNode document,
        params HttpResponseMessage[] answers)
    {
        var found = new List<string>();

        foreach (HttpResponseMessage answer in answers)
        {
            found.AddRange(ContractAgreement.Disagreements(
                document,
                Password,
                HttpMethods.Post,
                answer.StatusCode,
                await answer.Content.ReadAsStringAsync()));
        }

        return found;
    }
}
