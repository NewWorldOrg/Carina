using System.Buffers.Text;
using System.Text;
using System.Text.Json;

using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Auth;

public sealed class OidcGatewayTests
{
    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ADiscoveryDocumentIsReadForTheFourPlacesAndNothingElseIsCarriedAway()
    {
        await using var provider = new Harness();

        OidcEndpoints reached = (await provider.Gateway.ReachAsync(MockIdentityProvider.DiscoveryUrl, default))!;

        Assert.Equal(MockIdentityProvider.Issuer, reached.Issuer);
        Assert.Equal($"{MockIdentityProvider.Issuer}/authorize", reached.Authorization.ToString());
        Assert.Equal($"{MockIdentityProvider.Issuer}/token", reached.Token.ToString());
        Assert.Equal($"{MockIdentityProvider.Issuer}/jwks", reached.Jwks.ToString());
    }

    [Fact]
    public async Task AProviderOutOfReachIsAnAnswerRatherThanAThrow()
    {
        await using var provider = new Harness();
        provider.Idp.Reachable = false;

        Assert.Null(await provider.Gateway.ReachAsync(MockIdentityProvider.DiscoveryUrl, default));
    }

    [Fact]
    public async Task ADocumentServedFromSomewhereWithNoSuchPathIsNoDocument()
    {
        await using var provider = new Harness();

        Assert.Null(await provider.Gateway.ReachAsync($"{MockIdentityProvider.Issuer}/nothing-here", default));
    }

    [Fact]
    public async Task ATokenSignedByThePublishedKeyIsReadForEveryClaimTheRulesNeed()
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();
        string token = provider.Idp.Forge(new Dictionary<string, object>
        {
            ["iss"] = MockIdentityProvider.Issuer,
            ["aud"] = "carina",
            ["sub"] = "owner",
            ["exp"] = new DateTimeOffset(At).ToUnixTimeSeconds(),
            ["nonce"] = "a-nonce",
            ["groups"] = new[] { "operators" },
            ["hd"] = "example.test",
            ["email"] = "owner@example.test",
            ["name"] = "The Owner",
        });

        OidcClaims claims = (await provider.Gateway.ReadAsync(reached, token, default))!;

        Assert.Equal(MockIdentityProvider.Issuer, claims.Issuer);
        Assert.Equal(["carina"], claims.Audiences);
        Assert.Equal("owner", claims.Subject);
        Assert.Equal(At, claims.ExpiresAt);
        Assert.Equal("a-nonce", claims.Nonce);
        Assert.Equal(["operators"], claims.Groups);
        Assert.Equal("example.test", claims.HostedDomain);
        Assert.Equal("owner@example.test", claims.Email);
        Assert.Equal("The Owner", claims.Name);
        Assert.False(claims.GroupsOverflowed);
    }

    [Fact]
    public async Task BrAu018ATokenNamingNeitherAnEmailNorANameLeavesBothUnsetRatherThanGuessed()
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();
        string token = provider.Idp.Forge(new Dictionary<string, object>
        {
            ["iss"] = MockIdentityProvider.Issuer,
            ["aud"] = "carina",
            ["sub"] = "owner",
            ["exp"] = new DateTimeOffset(At).ToUnixTimeSeconds(),
        });

        OidcClaims claims = (await provider.Gateway.ReadAsync(reached, token, default))!;

        Assert.Null(claims.Email);
        Assert.Null(claims.Name);
        Assert.Equal("owner", claims.DisplayName);
    }

    [Fact]
    public async Task ATokenNamingSeveralAudiencesCarriesThemAll()
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();
        string token = provider.Idp.Forge(Claims(("aud", new[] { "carina", "somebody-else" })));

        OidcClaims claims = (await provider.Gateway.ReadAsync(reached, token, default))!;

        Assert.Equal(["carina", "somebody-else"], claims.Audiences);
    }

    [Fact]
    public async Task ATokenWhoseGroupsOverflowedSaysSoWithoutTheGroupsThemselves()
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();
        string token = provider.Idp.Forge(Claims(
            ("_claim_names", new Dictionary<string, string> { ["groups"] = "src1" })));

        OidcClaims claims = (await provider.Gateway.ReadAsync(reached, token, default))!;

        Assert.True(claims.GroupsOverflowed);
        Assert.Empty(claims.Groups);
    }

    [Fact]
    public async Task ATokenNamingOtherClaimsThatOverflowedIsNotOneWhoseGroupsDid()
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();
        string token = provider.Idp.Forge(Claims(
            ("_claim_names", new Dictionary<string, string> { ["roles"] = "src1" })));

        OidcClaims claims = (await provider.Gateway.ReadAsync(reached, token, default))!;

        Assert.False(claims.GroupsOverflowed);
    }

    [Fact]
    public async Task ATokenSignedWithAKeyTheProviderNeverPublishedIsNotRead()
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();
        provider.Idp.SignsWithAKeyItNeverPublished = true;

        Assert.Null(await provider.Gateway.ReadAsync(reached, provider.Idp.Forge(Claims()), default));
    }

    [Fact]
    public async Task ATokenWhosePayloadWasChangedAfterItWasSignedIsNotRead()
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();
        string[] parts = provider.Idp.Forge(Claims()).Split('.');
        string tampered = Base64Url.EncodeToString(
            JsonSerializer.SerializeToUtf8Bytes(Claims(("sub", "somebody-else"))));

        Assert.Null(await provider.Gateway.ReadAsync(reached, $"{parts[0]}.{tampered}.{parts[2]}", default));
    }

    [Fact]
    public async Task ATokenClaimingItNeedsNoSignatureIsNotRead()
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();
        string header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""));
        string payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(Claims()));

        Assert.Null(await provider.Gateway.ReadAsync(reached, $"{header}.{payload}.x", default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("one.two")]
    [InlineData("one.two.three.four")]
    [InlineData("...")]
    public async Task SomethingThatIsNotATokenAtAllIsNotRead(string token)
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();

        Assert.Null(await provider.Gateway.ReadAsync(reached, token, default));
    }

    [Fact]
    public async Task ATokenMissingTheClaimsTheRulesCompareAgainstIsNotRead()
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();
        string token = provider.Idp.Forge(new Dictionary<string, object>
        {
            ["iss"] = MockIdentityProvider.Issuer,
            ["aud"] = "carina",
        });

        Assert.Null(await provider.Gateway.ReadAsync(reached, token, default));
    }

    [Fact]
    public async Task ACodeIsSpentWithTheVerifierAndTheSecretTheProviderIssued()
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();
        PkceChallenge pkce = PkceChallenge.Issue();
        string code = provider.Idp.Authorize(
            Authorize(pkce, "a-nonce"),
            new MockIdentityUser("owner"));

        string idToken = (await provider.Gateway.ExchangeAsync(reached, Spending(code, pkce), default))!;

        Assert.Equal([provider.Idp.ClientSecret], provider.Idp.SecretsOffered);
        Assert.NotNull(await provider.Gateway.ReadAsync(reached, idToken, default));
    }

    [Fact]
    public async Task ACodeSpentWithSomebodyElsesVerifierBuysNothing()
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();
        PkceChallenge pkce = PkceChallenge.Issue();
        string code = provider.Idp.Authorize(Authorize(pkce, "a-nonce"), new MockIdentityUser("owner"));

        Assert.Null(await provider.Gateway.ExchangeAsync(
            reached,
            Spending(code, PkceChallenge.Issue()),
            default));
    }

    [Fact]
    public async Task ACodeSpentTwiceBuysNothingTheSecondTime()
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();
        PkceChallenge pkce = PkceChallenge.Issue();
        string code = provider.Idp.Authorize(Authorize(pkce, "a-nonce"), new MockIdentityUser("owner"));

        Assert.NotNull(await provider.Gateway.ExchangeAsync(reached, Spending(code, pkce), default));
        Assert.Null(await provider.Gateway.ExchangeAsync(reached, Spending(code, pkce), default));
    }

    [Fact]
    public async Task ACodeSpentWithTheWrongSecretBuysNothing()
    {
        await using var provider = new Harness();
        OidcEndpoints reached = await provider.ReachedAsync();
        PkceChallenge pkce = PkceChallenge.Issue();
        string code = provider.Idp.Authorize(Authorize(pkce, "a-nonce"), new MockIdentityUser("owner"));

        Assert.Null(await provider.Gateway.ExchangeAsync(
            reached,
            Spending(code, pkce) with { ClientSecret = new ClientSecret("the-wrong-secret") },
            default));
    }

    private static Uri Authorize(PkceChallenge pkce, string nonce)
        => new($"{MockIdentityProvider.Issuer}/authorize?state=a-state&nonce={nonce}"
               + $"&code_challenge={pkce.Challenge}&redirect_uri=https://carina.example/back");

    private static OidcCodeExchange Spending(string code, PkceChallenge pkce)
        => new(
            "carina",
            new ClientSecret("the-client-secret"),
            code,
            "https://carina.example/back",
            pkce.Verifier);

    private static Dictionary<string, object> Claims(params (string Name, object Value)[] extra)
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["iss"] = MockIdentityProvider.Issuer,
            ["aud"] = "carina",
            ["sub"] = "owner",
            ["exp"] = new DateTimeOffset(At).ToUnixTimeSeconds(),
            ["nonce"] = "a-nonce",
        };

        foreach ((string name, object value) in extra)
        {
            claims[name] = value;
        }

        return claims;
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly HttpClient client;

        public Harness()
        {
            client = new HttpClient(Idp);
            Gateway = new OidcGateway(client);
        }

        public MockIdentityProvider Idp { get; } = new();

        public OidcGateway Gateway { get; }

        public async Task<OidcEndpoints> ReachedAsync()
            => (await Gateway.ReachAsync(MockIdentityProvider.DiscoveryUrl, default))!;

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            Idp.Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
