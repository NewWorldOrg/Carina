using System.Globalization;

namespace Carina.Api.Playback;

public enum RangeAnswer
{
    Whole = 1,

    Part = 2,

    OutOfReach = 3,
}

public sealed record ByteRange
{
    public const string Unit = "bytes";

    private const string Prefix = "bytes=";

    private ByteRange(RangeAnswer answer, long from, long count)
    {
        Answer = answer;
        From = from;
        Count = count;
    }

    public RangeAnswer Answer { get; }

    public long From { get; }

    public long Count { get; }

    public long Last => From + Count - 1;

    public static ByteRange Read(string? asked, long size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        if (string.IsNullOrWhiteSpace(asked))
        {
            return Everything(size);
        }

        string trimmed = asked.Trim();

        if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Everything(size);
        }

        string spec = trimmed[Prefix.Length..].Trim();

        if (spec.Contains(',', StringComparison.Ordinal))
        {
            return Everything(size);
        }

        int dash = spec.IndexOf('-');

        if (dash < 0)
        {
            return Everything(size);
        }

        string firstText = spec[..dash].Trim();
        string lastText = spec[(dash + 1)..].Trim();

        return firstText.Length is 0
            ? FromTheEnd(lastText, size)
            : FromTheStart(firstText, lastText, size);
    }

    private static ByteRange FromTheEnd(string lastText, long size)
    {
        if (!Number(lastText, out long suffix))
        {
            return Everything(size);
        }

        if (suffix is 0 || size is 0)
        {
            return new ByteRange(RangeAnswer.OutOfReach, 0, 0);
        }

        long from = suffix >= size ? 0 : size - suffix;

        return new ByteRange(RangeAnswer.Part, from, size - from);
    }

    private static ByteRange FromTheStart(string firstText, string lastText, long size)
    {
        if (!Number(firstText, out long first))
        {
            return Everything(size);
        }

        long last = size - 1;

        if (lastText.Length is not 0)
        {
            if (!Number(lastText, out long asked))
            {
                return Everything(size);
            }

            if (asked < first)
            {
                return Everything(size);
            }

            last = Math.Min(asked, size - 1);
        }

        return size is 0 || first >= size
            ? new ByteRange(RangeAnswer.OutOfReach, 0, 0)
            : new ByteRange(RangeAnswer.Part, first, last - first + 1);
    }

    private static ByteRange Everything(long size) => new(RangeAnswer.Whole, 0, size);

    private static bool Number(string text, out long read)
        => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out read);
}
