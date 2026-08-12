using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carina.Contracts;

/// <summary>
/// Identifies a session for its whole life, across app restarts.
/// </summary>
/// <remarks>
/// The value goes into a request path on the privileged process, so the shape is
/// constrained: anything outside the allowed characters can never be turned into a
/// path, which is what keeps one endpoint's request from becoming another's.
///
/// Reading is separate from using. A driver may mint an identifier this build would
/// not have minted, and losing the whole answer over one such value would break the
/// rule that neither side fails on what the other says — so an identifier outside
/// the shape reads as <see cref="IsUnset"/> and the rest of the message survives.
/// What it cannot do is become a path.
/// </remarks>
[JsonConverter(typeof(SessionIdJsonConverter))]
public readonly record struct SessionId
{
    /// <summary>The longest value this build can put in a path.</summary>
    public const int MaxLength = 64;

    private SessionId(string value) => Value = value;

    /// <summary>The identifier itself, or null when this build could not take it.</summary>
    public string? Value { get; }

    /// <summary>Whether there is no identifier this build can act on.</summary>
    public bool IsUnset => Value is null;

    /// <summary>Reads <paramref name="value"/>, rejecting anything outside <c>[A-Za-z0-9-]</c>.</summary>
    public static SessionId Parse(string? value) =>
        TryParse(value, out var id)
            ? id
            : throw new FormatException(
                $"A session id must be 1 to {MaxLength} characters of A-Z, a-z, 0-9 or '-'; got '{value}'."
            );

    /// <summary>Reads <paramref name="value"/>, reporting failure instead of throwing.</summary>
    public static bool TryParse(string? value, out SessionId id)
    {
        id = default;
        if (value is null || value.Length is 0 or > MaxLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            var allowed =
                c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-';
            if (!allowed)
            {
                return false;
            }
        }

        id = new SessionId(value);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Carries <see cref="SessionId"/> as a plain JSON string.
/// </summary>
/// <remarks>
/// Anything that is not an identifier this build can act on — absent, null, or
/// outside the shape — reads as unset rather than failing the message it sits in.
/// </remarks>
public sealed class SessionIdJsonConverter : JsonConverter<SessionId>
{
    /// <inheritdoc />
    public override SessionId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType is JsonTokenType.String
        && SessionId.TryParse(reader.GetString(), out var id)
            ? id
            : default;

    /// <inheritdoc />
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
