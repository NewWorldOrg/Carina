using Carina.Domain.Machines;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class MachineOptions
{
    public const string Section = "Machine";

    public string? RenderNode { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        RenderNode = configuration.GetSection(Section)[nameof(RenderNode)];
    }

    public MachineSettings Read()
    {
        MachineSettings unset = new();

        return unset with { RenderNode = Absolute(RenderNode, nameof(RenderNode), unset.RenderNode) };
    }

    private static string Absolute(string? setting, string name, string unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        return setting.StartsWith('/') && setting.Trim() == setting
            ? setting
            : throw new ArgumentException(
                $"{Section}:{name} names the render node as an absolute path with no surrounding space, and '{setting}' is not one.",
                name);
    }
}

public sealed class MachineValidation : IValidateOptions<MachineOptions>
{
    public ValidateOptionsResult Validate(string? name, MachineOptions options)
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
