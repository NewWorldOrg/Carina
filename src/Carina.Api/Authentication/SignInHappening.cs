using Carina.Domain.Auth;

namespace Carina.Api.Authentication;

/// <summary>What a way in did with the request it answered, left on the request for the gate to write down.</summary>
public sealed record SignInHappening(SignInMoment Moment, AuthMethod? Method, string? Device, string? Reason)
{
    public static void Leave(
        HttpContext context,
        SignInMoment moment,
        AuthMethod? method = null,
        string? device = null,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Features.Set(new SignInHappening(moment, method, device, reason));
    }

    public static SignInHappening? LeftOn(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Features.Get<SignInHappening>();
    }
}
