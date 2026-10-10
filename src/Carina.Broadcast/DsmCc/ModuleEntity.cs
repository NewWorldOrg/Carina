using System.Text;

namespace Carina.Broadcast.DsmCc;

internal static class ModuleEntity
{
    private static readonly ModuleContentRead Malformed = new ModuleContentRead.Rejected(CarouselDefect.EntityMalformed);

    public static ModuleContentRead Split(ReadOnlyMemory<byte> entity, string? moduleType, string? moduleName, int mostParts)
    {
        MimeHeader? header = null;
        int start = 0;

        if (MimeHeader.StartsWithField(entity.Span) && !MimeHeader.TryRead(entity.Span, out header, out start))
        {
            return Malformed;
        }

        string? contentType = header?[MimeHeader.ContentType] ?? moduleType;
        MediaType media = MediaType.Parse(contentType ?? string.Empty);

        if (!media.IsMultipart)
        {
            string location = header?[MimeHeader.ContentLocation] ?? moduleName ?? string.Empty;

            return new ModuleContentRead.Opened([ModuleResource.Of(location, contentType, entity[start..])]);
        }

        return string.IsNullOrEmpty(media.Boundary)
            ? Malformed
            : SplitParts(entity, start, Encoding.Latin1.GetBytes($"--{media.Boundary}"), mostParts);
    }

    private static ModuleContentRead SplitParts(ReadOnlyMemory<byte> entity, int from, byte[] delimiter, int mostParts)
    {
        ReadOnlySpan<byte> span = entity.Span;
        var parts = new List<ModuleResource>();
        int at = NextDelimiter(span, delimiter, from);

        while (at >= 0)
        {
            int after = at + delimiter.Length;

            if (span[after..].StartsWith("--"u8))
            {
                return new ModuleContentRead.Opened(parts);
            }

            if (parts.Count >= mostParts)
            {
                return new ModuleContentRead.Rejected(CarouselDefect.TooManyParts);
            }

            int lineEnd = span[after..].IndexOf((byte)'\n');
            int partStart = after + lineEnd + 1;
            int next = lineEnd < 0 ? -1 : NextDelimiter(span, delimiter, partStart);

            if (next < 0 || !TryTakePart(entity[partStart..next], parts))
            {
                return Malformed;
            }

            at = next;
        }

        return Malformed;
    }

    private static bool TryTakePart(ReadOnlyMemory<byte> part, List<ModuleResource> parts)
    {
        if (!MimeHeader.TryRead(part.Span, out MimeHeader? header, out int bodyStart))
        {
            return false;
        }

        int bodyEnd = Math.Max(bodyStart, WithoutLineBreak(part.Span, part.Length));

        parts.Add(ModuleResource.Of(
            header[MimeHeader.ContentLocation] ?? string.Empty,
            header[MimeHeader.ContentType],
            part[bodyStart..bodyEnd]));

        return true;
    }

    private static int NextDelimiter(ReadOnlySpan<byte> entity, ReadOnlySpan<byte> delimiter, int from)
    {
        int at = from;

        while (at < entity.Length)
        {
            int found = entity[at..].IndexOf(delimiter);

            if (found < 0)
            {
                return -1;
            }

            int position = at + found;

            if ((position == 0 || entity[position - 1] == (byte)'\n') && EndsTheLine(entity[(position + delimiter.Length)..]))
            {
                return position;
            }

            at = position + 1;
        }

        return -1;
    }

    private static bool EndsTheLine(ReadOnlySpan<byte> afterTheBoundary)
    {
        ReadOnlySpan<byte> rest = afterTheBoundary.StartsWith("--"u8) ? afterTheBoundary[2..] : afterTheBoundary;
        int lineEnd = rest.IndexOf((byte)'\n');
        ReadOnlySpan<byte> padding = lineEnd < 0 ? rest : rest[..lineEnd];

        return padding.IndexOfAnyExcept(" \t\r"u8) < 0;
    }

    private static int WithoutLineBreak(ReadOnlySpan<byte> part, int end)
    {
        int trimmed = end;

        if (trimmed > 0 && part[trimmed - 1] == (byte)'\n')
        {
            trimmed--;
        }

        if (trimmed > 0 && part[trimmed - 1] == (byte)'\r')
        {
            trimmed--;
        }

        return trimmed;
    }
}
