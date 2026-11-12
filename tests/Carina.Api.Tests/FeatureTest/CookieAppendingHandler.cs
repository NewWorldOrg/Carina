using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using SameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class CookieAppendingHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "CookieProbe";

    public const string CookieName = "carina-probe";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Response.Cookies.Append(
            CookieName,
            "issued",
            new CookieOptions
            {
                SameSite = SameSiteMode.None,
                HttpOnly = false,
                Path = "/",
            });

        return Task.FromResult(AuthenticateResult.NoResult());
    }
}
