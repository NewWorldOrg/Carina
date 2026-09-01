using Carina.Domain.Base;

namespace Carina.Domain.Streaming;

public sealed class StreamSource : CommonValueObject<string>
{
    public StreamSource(string value)
        : base(Validated(value))
    {
    }

    private static string Validated(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.StartsWith('-'))
        {
            throw new ArgumentException(
                "A source that begins with a dash is read as an option by the programme it is handed to.",
                nameof(value));
        }

        foreach (char letter in value)
        {
            if (char.IsControl(letter))
            {
                throw new ArgumentException(
                    "A source is a name this application built, so a control character in one means text from the broadcast reached it.",
                    nameof(value));
            }
        }

        return value;
    }
}
