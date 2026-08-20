using Microsoft.Extensions.Options;

namespace Carina.Api.Authentication;

public sealed class PublicOriginOptions
{
    public string? Origin { get; set; }

    public PublicOrigin Read() => PublicOrigin.Named(Origin);
}

public sealed class PublicOriginValidation : IValidateOptions<PublicOriginOptions>
{
    public ValidateOptionsResult Validate(string? name, PublicOriginOptions options)
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
