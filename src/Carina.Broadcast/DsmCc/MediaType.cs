using System.Text;

namespace Carina.Broadcast.DsmCc;

internal sealed class MediaType
{
    private readonly Dictionary<string, string> parameters;

    private MediaType(string type, Dictionary<string, string> parameters)
    {
        Type = type;
        this.parameters = parameters;
    }

    public string Type { get; }

    public bool IsMultipart => Type.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase);

    public string? Boundary => parameters.GetValueOrDefault("boundary");

    public string? Charset => parameters.GetValueOrDefault("charset");

    public static MediaType Parse(string value)
    {
        IReadOnlyList<string> pieces = Split(value);
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string piece in pieces.Skip(1))
        {
            int equals = piece.IndexOf('=', StringComparison.Ordinal);

            if (equals > 0)
            {
                parameters.TryAdd(piece[..equals].Trim(), Unquoted(piece[(equals + 1)..].Trim()));
            }
        }

        return new MediaType(pieces[0].Trim(), parameters);
    }

    private static List<string> Split(string value)
    {
        var pieces = new List<string>();
        var piece = new StringBuilder();
        bool quoted = false;

        foreach (char character in value)
        {
            quoted ^= character == '"';

            if (character == ';' && !quoted)
            {
                pieces.Add(piece.ToString());
                piece.Clear();

                continue;
            }

            piece.Append(character);
        }

        pieces.Add(piece.ToString());

        return pieces;
    }

    private static string Unquoted(string value)
        => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}
