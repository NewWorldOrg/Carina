using Carina.Infrastructure.Segments;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class LearningOptions
{
    public const string Section = "Learning";

    public string? ImportFrom { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        ImportFrom = configuration.GetSection(Section)[nameof(ImportFrom)];
    }

    public LearningImportSettings Read() => new() { ImportFrom = Absolute(ImportFrom, nameof(ImportFrom)) };

    private static string? Absolute(string? setting, string name)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return null;
        }

        return setting.StartsWith('/')
            ? setting
            : throw new ArgumentException(
                $"{Section}:{name} is read where the process can reach it, and '{setting}' is not absolute.",
                name);
    }
}

public sealed class LearningValidation : IValidateOptions<LearningOptions>
{
    public ValidateOptionsResult Validate(string? name, LearningOptions options)
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
