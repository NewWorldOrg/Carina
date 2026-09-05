using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.WebUtilities;

namespace Carina.TestSupport;

public sealed record MockIdentityUser(string Subject)
{
    public IReadOnlyList<string> Groups { get; init; } = [];

    public bool GroupsOverflowed { get; init; }

    public string? HostedDomain { get; init; }

    public string? Email { get; init; }

    public string? Name { get; init; }
}

public sealed class MockIdentityProvider : HttpMessageHandler
{
    public const string Issuer = "https://login.example.test";

    public const string DiscoveryUrl = $"{Issuer}/.well-known/openid-configuration";

    public const string AuthorizePath = "/authorize";

    public const string TokenPath = "/token";

    public const string JwksPath = "/jwks";

    public const string SignOutPath = "/logout";

    public const string KeyId = "the-only-key";

    private readonly RSA published = RSA.Create(2048);

    private readonly RSA unpublished = RSA.Create(2048);

    private readonly Dictionary<string, GrantedCode> granted = new(StringComparer.Ordinal);

    public string ClientId { get; set; } = "carina";

    public string ClientSecret { get; set; } = "the-client-secret";

    public bool Reachable { get; set; } = true;

    public bool SignsWithAKeyItNeverPublished { get; set; }

    public string? IssuerOverride { get; set; }

    public string? AudienceOverride { get; set; }

    public string? NonceOverride { get; set; }

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    public TimeProvider Clock { get; set; } = TimeProvider.System;

    public List<string> Visits { get; } = [];

    public List<string> SecretsOffered { get; } = [];

    public string Authorize(Uri redirected, MockIdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(redirected);
        ArgumentNullException.ThrowIfNull(user);

        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> asked =
            QueryHelpers.ParseQuery(redirected.Query);

        string code = $"code-{granted.Count}-{Guid.NewGuid():N}";

        granted[code] = new GrantedCode(
            user,
            asked["nonce"].ToString(),
            asked["code_challenge"].ToString(),
            asked["redirect_uri"].ToString(),
            Spent: false,
            Lapsed: false);

        return code;
    }

    public static string StateOf(Uri redirected)
    {
        ArgumentNullException.ThrowIfNull(redirected);

        return QueryHelpers.ParseQuery(redirected.Query)["state"].ToString();
    }

    public void LetEveryCodeLapse()
    {
        foreach (string code in granted.Keys.ToArray())
        {
            granted[code] = granted[code] with { Lapsed = true };
        }
    }

    public string Forge(IReadOnlyDictionary<string, object> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        return Sign(new Dictionary<string, object>(claims, StringComparer.Ordinal));
    }

    public bool WasVisited(string path)
        => Visits.Any(visit => visit.EndsWith($" {path}", StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string path = request.RequestUri!.AbsolutePath;

        Visits.Add($"{request.Method.Method} {path}");

        if (!Reachable)
        {
            throw new HttpRequestException("The identity provider is out of reach.");
        }

        return path switch
        {
            "/.well-known/openid-configuration" => Json(Discovery()),
            JwksPath => Json(Keys()),
            TokenPath => await TokenAsync(request, cancellationToken),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            published.Dispose();
            unpublished.Dispose();
        }

        base.Dispose(disposing);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage Refused(string error)
        => new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, string> { ["error"] = error }),
                Encoding.UTF8,
                "application/json"),
        };

    private static string Discovery()
        => JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["issuer"] = Issuer,
            ["authorization_endpoint"] = $"{Issuer}{AuthorizePath}",
            ["token_endpoint"] = $"{Issuer}{TokenPath}",
            ["jwks_uri"] = $"{Issuer}{JwksPath}",
            ["end_session_endpoint"] = $"{Issuer}{SignOutPath}",
        });

    private async Task<HttpResponseMessage> TokenAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> offered = QueryHelpers.ParseQuery(
            await request.Content!.ReadAsStringAsync(cancellationToken));

        SecretsOffered.Add(offered["client_secret"].ToString());

        if (offered["client_id"].ToString() != ClientId || offered["client_secret"].ToString() != ClientSecret)
        {
            return Refused("invalid_client");
        }

        string code = offered["code"].ToString();

        if (!granted.TryGetValue(code, out GrantedCode? held) || held.Spent || held.Lapsed)
        {
            return Refused("invalid_grant");
        }

        if (held.RedirectUri != offered["redirect_uri"].ToString())
        {
            return Refused("invalid_grant");
        }

        string digested = Base64Url.EncodeToString(
            SHA256.HashData(Encoding.ASCII.GetBytes(offered["code_verifier"].ToString())));

        if (digested != held.Challenge)
        {
            return Refused("invalid_grant");
        }

        granted[code] = held with { Spent = true };

        return Json(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["token_type"] = "Bearer",
            ["access_token"] = "an-access-token-nobody-reads",
            ["id_token"] = IdToken(held),
        }));
    }

    private string Keys()
    {
        RSAParameters material = published.ExportParameters(includePrivateParameters: false);

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["keys"] =
            new[]
            {
                new Dictionary<string, string>
                {
                    ["kty"] = "RSA",
                    ["use"] = "sig",
                    ["alg"] = "RS256",
                    ["kid"] = KeyId,
                    ["n"] = Base64Url.EncodeToString(material.Modulus!),
                    ["e"] = Base64Url.EncodeToString(material.Exponent!),
                },
            },
        });
    }

    private string IdToken(GrantedCode held)
    {
        DateTimeOffset now = Clock.GetUtcNow();
        var claims = new Dictionary<string, object>
        {
            ["iss"] = IssuerOverride ?? Issuer,
            ["aud"] = AudienceOverride ?? ClientId,
            ["sub"] = held.User.Subject,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.Add(TokenLifetime).ToUnixTimeSeconds(),
            ["nonce"] = NonceOverride ?? held.Nonce,
        };

        if (held.User.Groups.Count > 0)
        {
            claims["groups"] = held.User.Groups;
        }

        if (held.User.GroupsOverflowed)
        {
            claims["_claim_names"] = new Dictionary<string, string> { ["groups"] = "src1" };
            claims["_claim_sources"] = new Dictionary<string, object>
            {
                ["src1"] = new Dictionary<string, string> { ["endpoint"] = $"{Issuer}/groups" },
            };
        }

        if (held.User.HostedDomain is { } hosted)
        {
            claims["hd"] = hosted;
        }

        if (held.User.Email is { } email)
        {
            claims["email"] = email;
        }

        if (held.User.Name is { } name)
        {
            claims["name"] = name;
        }

        return Sign(claims);
    }

    private string Sign(Dictionary<string, object> claims)
    {
        string header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, string> { ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = KeyId }));
        string payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(claims));
        RSA signer = SignsWithAKeyItNeverPublished ? unpublished : published;
        string signature = Base64Url.EncodeToString(signer.SignData(
            Encoding.ASCII.GetBytes($"{header}.{payload}"),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{header}.{payload}.{signature}");
    }

    private sealed record GrantedCode(
        MockIdentityUser User,
        string Nonce,
        string Challenge,
        string RedirectUri,
        bool Spent,
        bool Lapsed);
}
