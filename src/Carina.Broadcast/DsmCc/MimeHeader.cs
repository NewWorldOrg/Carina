using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Carina.Broadcast.DsmCc;

internal sealed class MimeHeader
{
    public const string ContentType = "Content-Type";

    public const string ContentLocation = "Content-Location";

    public const int MostHeaderBytes = 64 * 1024;

    private const string RepeatedField = "";

    private const byte LineFeed = (byte)'\n';

    private const byte CarriageReturn = (byte)'\r';

    private readonly Dictionary<string, string> fields;
    private readonly HashSet<string> repeated;

    private MimeHeader(Dictionary<string, string> fields, HashSet<string> repeated)
    {
        this.fields = fields;
        this.repeated = repeated;
    }

    public string? this[string name] => fields.GetValueOrDefault(name);

    public bool Repeats(string name) => repeated.Contains(name);

    public static bool TryRead(ReadOnlySpan<byte> entity, [NotNullWhen(true)] out MimeHeader? header, out int bodyStart)
    {
        header = null;
        bodyStart = 0;
        ReadOnlySpan<byte> bounded = entity[..Math.Min(entity.Length, MostHeaderBytes)];
        var fields = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        var repeated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? last = null;
        int at = 0;

        while (TryTakeLine(bounded, ref at, out ReadOnlySpan<byte> line))
        {
            if (line.IsEmpty)
            {
                header = new MimeHeader(
                    fields.ToDictionary(field => field.Key, field => field.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                    repeated);
                bodyStart = at;

                return true;
            }

            if (!TryTakeField(line, fields, repeated, ref last))
            {
                return false;
            }
        }

        return false;
    }

    public static bool StartsWithField(ReadOnlySpan<byte> entity)
    {
        int at = 0;

        if (!TryTakeLine(entity, ref at, out ReadOnlySpan<byte> line) || line.IsEmpty || line[0] is (byte)' ' or (byte)'\t')
        {
            return false;
        }

        int colon = line.IndexOf((byte)':');

        return colon > 0 && IsToken(line[..colon]);
    }

    private static bool TryTakeLine(ReadOnlySpan<byte> entity, ref int at, out ReadOnlySpan<byte> line)
    {
        int end = entity[at..].IndexOf(LineFeed);

        if (end < 0)
        {
            line = default;

            return false;
        }

        line = entity.Slice(at, end);
        at += end + 1;

        if (!line.IsEmpty && line[^1] == CarriageReturn)
        {
            line = line[..^1];
        }

        return true;
    }

    private static bool TryTakeField(ReadOnlySpan<byte> line, Dictionary<string, StringBuilder> fields, HashSet<string> repeated, ref string? last)
    {
        if (line[0] is (byte)' ' or (byte)'\t')
        {
            if (last is null)
            {
                return false;
            }

            if (last != RepeatedField)
            {
                fields[last].Append(' ').Append(Encoding.Latin1.GetString(line).Trim());
            }

            return true;
        }

        int colon = line.IndexOf((byte)':');

        if (colon <= 0 || !IsToken(line[..colon]))
        {
            return false;
        }

        string name = Encoding.Latin1.GetString(line[..colon]);

        last = fields.TryAdd(name, new StringBuilder(Encoding.Latin1.GetString(line[(colon + 1)..]).Trim())) ? name : RepeatedField;

        if (last == RepeatedField)
        {
            repeated.Add(name);
        }

        return true;
    }

    private static bool IsToken(ReadOnlySpan<byte> name)
    {
        foreach (byte character in name)
        {
            if (character is <= 0x20 or >= 0x7F || "()<>@,;:\\\"/[]?={}"u8.Contains(character))
            {
                return false;
            }
        }

        return true;
    }
}
