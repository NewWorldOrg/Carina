using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carina.Contracts;

[JsonConverter(typeof(SessionIdJsonConverter))]
public readonly record struct SessionId
{
    public static readonly int MaxLength = 64;

    private SessionId(string value) => Value = value;

    public string? Value { get; }

    public bool IsUnset => Value is null;

    public static SessionId Parse(string? value) =>
        TryParse(value, out SessionId id)
            ? id
            : throw new FormatException(
                $"A session id must be 1 to {MaxLength} characters of A-Z, a-z, 0-9 or '-'; got '{value}'."
            );

    public static bool TryParse(string? value, out SessionId id)
    {
        id = default;
        if (value is null || value.Length is 0 || value.Length > MaxLength)
        {
            return false;
        }

        foreach (char c in value)
        {
            bool allowed =
                c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-';
            if (!allowed)
            {
                return false;
            }
        }

        id = new SessionId(value);
        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}

public sealed class SessionIdJsonConverter : JsonConverter<SessionId>
{
    public override SessionId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType is JsonTokenType.String)
        {
            return SessionId.TryParse(reader.GetString(), out SessionId id) ? id : default;
        }

        if (
            reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray
            && !reader.TrySkip()
        )
        {
            throw new JsonException(
                "A structured session id could not be consumed within one buffer."
            );
        }

        return default;
    }

    public override void Write(
        Utf8JsonWriter writer,
        SessionId value,
        JsonSerializerOptions options
    )
    {
        if (value.Value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value);
    }
}
