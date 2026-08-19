using Microsoft.AspNetCore.CookiePolicy;

namespace Carina.Api.Authentication;

public static class SessionCookiePolicy
{
    public static CookiePolicyOptions Options { get; } = new()
    {
        MinimumSameSitePolicy = SameSiteMode.Lax,
        HttpOnly = HttpOnlyPolicy.Always,
        Secure = CookieSecurePolicy.SameAsRequest,
    };
}
