using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Carina.Broadcast.DsmCc;

internal sealed class MimeHeader
{
    public const string ContentType = "Content-Type";

    public const string ContentLocation = "Content-Location";

    private const string RepeatedField = "";

    private const byte LineFeed = (byte)'\n';

    private const byte CarriageReturn = (byte)'\r';

    private readonly Dictionary<string, string> fields;

    private MimeHeader(Dictionary<string, string> fields)
    {
        this.fields = fields;
    }

    public string? this[string name] => fields.GetValueOrDefault(name);

    public static bool TryRead(ReadOnlySpan<byte> entity, [NotNullWhen(true)] out MimeHeader? header, out int bodyStart)
    {
        header = null;
        bodyStart = 0;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? last = null;
        int at = 0;

        while (TryTakeLine(entity, ref at, out ReadOnlySpan<byte> line))
        {
            if (line.IsEmpty)
            {
                header = new MimeHeader(fields);
                bodyStart = at;

                return true;
            }

            if (!TryTakeField(line, fields, ref last))
            {
                return false;
            }
        }

        return false;
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

    private static bool TryTakeField(ReadOnlySpan<byte> line, Dictionary<string, string> fields, ref string? last)
    {
        if (line[0] is (byte)' ' or (byte)'\t')
        {
            if (last is null)
            {
                return false;
            }

            if (last != RepeatedField)
            {
                fields[last] = $"{fields[last]} {Encoding.Latin1.GetString(line).Trim()}";
            }

            return true;
        }

        int colon = line.IndexOf((byte)':');

        if (colon <= 0 || !IsToken(line[..colon]))
        {
            return false;
        }

        string name = Encoding.Latin1.GetString(line[..colon]);

        last = fields.TryAdd(name, Encoding.Latin1.GetString(line[(colon + 1)..]).Trim()) ? name : RepeatedField;

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
