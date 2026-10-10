using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Carina.Broadcast.DsmCc;

internal static class ModuleEntity
{
    public static bool TrySplit(
        ReadOnlyMemory<byte> entity,
        string? moduleType,
        string? moduleName,
        [NotNullWhen(true)] out IReadOnlyList<ModuleResource>? resources)
    {
        resources = null;
        MimeHeader? header = TypedHeader(entity.Span, out int start);
        string? contentType = header?[MimeHeader.ContentType] ?? moduleType;
        MediaType media = MediaType.Parse(contentType ?? string.Empty);

        if (!media.IsMultipart)
        {
            string location = header?[MimeHeader.ContentLocation] ?? moduleName ?? string.Empty;
            resources = [ModuleResource.Of(location, contentType, entity[start..])];

            return true;
        }

        var parts = new List<ModuleResource>();

        if (string.IsNullOrEmpty(media.Boundary) || !TrySplitParts(entity, start, Encoding.Latin1.GetBytes($"--{media.Boundary}"), parts))
        {
            return false;
        }

        resources = parts;

        return true;
    }

    private static MimeHeader? TypedHeader(ReadOnlySpan<byte> entity, out int bodyStart)
    {
        if (MimeHeader.TryRead(entity, out MimeHeader? header, out bodyStart) && header[MimeHeader.ContentType] is not null)
        {
            return header;
        }

        bodyStart = 0;

        return null;
    }

    private static bool TrySplitParts(ReadOnlyMemory<byte> entity, int from, byte[] delimiter, List<ModuleResource> parts)
    {
        ReadOnlySpan<byte> span = entity.Span;
        int at = NextDelimiter(span, delimiter, from);

        while (at >= 0)
        {
            int after = at + delimiter.Length;

            if (span[after..].StartsWith("--"u8))
            {
                return true;
            }

            int lineEnd = span[after..].IndexOf((byte)'\n');
            int partStart = after + lineEnd + 1;
            int next = lineEnd < 0 ? -1 : NextDelimiter(span, delimiter, partStart);

            if (next < 0 || !TryTakePart(entity[partStart..WithoutLineBreak(span, partStart, next)], parts))
            {
                return false;
            }

            at = next;
        }

        return false;
    }

    private static bool TryTakePart(ReadOnlyMemory<byte> part, List<ModuleResource> parts)
    {
        if (!MimeHeader.TryRead(part.Span, out MimeHeader? header, out int bodyStart))
        {
            return false;
        }

        parts.Add(ModuleResource.Of(
            header[MimeHeader.ContentLocation] ?? string.Empty,
            header[MimeHeader.ContentType],
            part[bodyStart..]));

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

            if (position == 0 || entity[position - 1] == (byte)'\n')
            {
                return position;
            }

            at = position + 1;
        }

        return -1;
    }

    private static int WithoutLineBreak(ReadOnlySpan<byte> entity, int start, int end)
    {
        int trimmed = end;

        if (trimmed > start && entity[trimmed - 1] == (byte)'\n')
        {
            trimmed--;
        }

        if (trimmed > start && entity[trimmed - 1] == (byte)'\r')
        {
            trimmed--;
        }

        return trimmed;
    }
}
