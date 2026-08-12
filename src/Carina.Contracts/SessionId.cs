using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carina.Contracts;

/// <summary>
/// Identifies a session for its whole life, across app restarts.
/// </summary>
/// <remarks>
/// The value goes into a request path on the privileged process, so the shape is
/// constrained rather than trusted: anything outside the allowed characters is
/// rejected where it enters, not escaped where it is used. That keeps a hostile or
/// merely careless value from turning one endpoint's request into another's.
/// </remarks>
[JsonConverter(typeof(SessionIdJsonConverter))]
public readonly record struct SessionId
{
    /// <summary>The longest value the driver will mint or accept.</summary>
    public const int MaxLength = 64;

    private SessionId(string value) => Value = value;

    /// <summary>The identifier itself.</summary>
    public string Value { get; }

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

/// <summary>Carries <see cref="SessionId"/> as a plain JSON string.</summary>
public sealed class SessionIdJsonConverter : JsonConverter<SessionId>
{
    /// <inheritdoc />
    public override SessionId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        SessionId.TryParse(reader.GetString(), out var id)
            ? id
            : throw new JsonException("The session id is not in the allowed shape.");

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        SessionId value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.Value);
}
