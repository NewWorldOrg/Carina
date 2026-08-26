using System.Globalization;

using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Configuration;

public sealed class RecordingOptions
{
    public const string Section = "Recording";

    public string? BeforeFirstTick { get; set; }

    public string? BetweenTicks { get; set; }

    public string? TuningLead { get; set; }

    public string? OutputRoot { get; set; }

    public void ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection named = configuration.GetSection(Section);

        BeforeFirstTick = named[nameof(BeforeFirstTick)];
        BetweenTicks = named[nameof(BetweenTicks)];
        TuningLead = named[nameof(TuningLead)];
        OutputRoot = named[nameof(OutputRoot)];
    }

    public RecordingSettings Read()
    {
        RecordingSettings unset = RecordingSettings.Default;

        try
        {
            return new RecordingSettings(
                Positive(BeforeFirstTick, nameof(BeforeFirstTick), unset.BeforeFirstTick),
                Positive(BetweenTicks, nameof(BetweenTicks), unset.BetweenTicks),
                Positive(TuningLead, nameof(TuningLead), unset.TuningLead),
                Named(unset.OutputRoot));
        }
        catch (ArgumentOutOfRangeException refusal)
        {
            throw new ArgumentException(
                $"{Section} does not describe a recorder that can run: {refusal.Message}",
                refusal.ParamName,
                refusal);
        }
    }

    private OutputRoot Named(OutputRoot unset)
    {
        if (string.IsNullOrWhiteSpace(OutputRoot))
        {
            return unset;
        }

        try
        {
            return new OutputRoot(OutputRoot.Trim());
        }
        catch (ArgumentException refusal)
        {
            throw new ArgumentException(
                $"{Section}:{nameof(OutputRoot)} names one of the output roots the driver declares: {refusal.Message}",
                nameof(OutputRoot),
                refusal);
        }
    }

    private static TimeSpan Positive(string? setting, string name, TimeSpan unset)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return unset;
        }

        TimeSpan read = TimeSpan.TryParse(setting, CultureInfo.InvariantCulture, out TimeSpan parsed)
            ? parsed
            : throw new ArgumentException(
                $"{Section}:{name} reads a duration as [d.]hh:mm:ss, and '{setting}' is not one.",
                name);

        return read > TimeSpan.Zero
            ? read
            : throw new ArgumentException($"{Section}:{name} has to be longer than nothing.", name);
    }
}

public sealed class RecordingValidation : IValidateOptions<RecordingOptions>
{
    public ValidateOptionsResult Validate(string? name, RecordingOptions options)
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
