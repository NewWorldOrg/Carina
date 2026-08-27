using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

public sealed class RecordingStopReason : CommonValueObject<string>
{
    public const int MaxLength = 500;

    public RecordingStopReason(string value)
        : base(Validated(value))
    {
    }

    public static RecordingStopReason? Read(string? asked)
        => Usable(asked) is { } reason ? new RecordingStopReason(reason) : null;

    private static string Validated(string value)
        => Usable(value)
           ?? throw new ArgumentException(
               $"A recording is stopped for a reason somebody can read afterwards: at least one letter, "
               + $"at most {MaxLength}, and nothing a terminal would act on rather than show.",
               nameof(value));

    private static string? Usable(string? asked)
    {
        if (string.IsNullOrWhiteSpace(asked))
        {
            return null;
        }

        string trimmed = asked.Trim();

        if (trimmed.Length > MaxLength || trimmed.Any(char.IsControl))
        {
            return null;
        }

        return trimmed;
    }
}
