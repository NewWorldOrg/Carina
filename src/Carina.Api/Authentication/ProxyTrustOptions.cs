using Microsoft.Extensions.Options;

namespace Carina.Api.Authentication;

public sealed class ProxyTrustOptions
{
    public string? KnownProxies { get; set; }

    public string? KnownNetworks { get; set; }

    public TrustedProxies Read() => TrustedProxies.Named(KnownProxies, KnownNetworks);
}

public sealed class ProxyTrustValidation : IValidateOptions<ProxyTrustOptions>
{
    public ValidateOptionsResult Validate(string? name, ProxyTrustOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            options.Read();
        }
        catch (ArgumentException refusal)
        {
            return ValidateOptionsResult.Fail(refusal.Message);
        }

        return ValidateOptionsResult.Success;
    }
}
