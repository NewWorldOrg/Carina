using Microsoft.Extensions.Options;

namespace Carina.Api.Authentication;

public sealed class AnonymousNetworkOptions
{
    public string? Networks { get; set; }

    public AnonymousNetworks Read() => AnonymousNetworks.Named(Networks);
}

public sealed class AnonymousNetworkValidation : IValidateOptions<AnonymousNetworkOptions>
{
    public ValidateOptionsResult Validate(string? name, AnonymousNetworkOptions options)
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
