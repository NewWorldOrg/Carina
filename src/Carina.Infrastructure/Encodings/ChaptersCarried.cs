using System.Globalization;
using System.Text.Json;

using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

public sealed record CarriedChapter(TimeSpan Starts, TimeSpan Ends, string Title);

public static class ChaptersCarried
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(10);

    public static IReadOnlyList<CarriedChapter>? Read(string said)
    {
        ArgumentNullException.ThrowIfNull(said);

        try
        {
            using JsonDocument document = JsonDocument.Parse(said);

            return Of(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? Differs(IReadOnlyList<CarriedChapter> carried, IReadOnlyList<ChapterSegment> wanted)
    {
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(wanted);

        if (carried.Count != wanted.Count)
        {
            return string.Create(CultureInfo.InvariantCulture, $"it carries {carried.Count} chapter(s) where {wanted.Count} were written");
        }

        int at = carried.Zip(wanted).TakeWhile(pair => Agree(pair.First, pair.Second)).Count();

        return at == carried.Count
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"its chapter {at} runs from {carried[at].Starts.TotalSeconds:0.###} s to {carried[at].Ends.TotalSeconds:0.###} s as '{carried[at].Title}', where it was written from {wanted[at].Starts.TotalSeconds:0.###} s to {wanted[at].Ends.TotalSeconds:0.###} s as {wanted[at].Kind}");
    }

    private static bool Agree(CarriedChapter carried, ChapterSegment wanted)
        => (carried.Starts - wanted.Starts).Duration() <= Tolerance
           && (carried.Ends - wanted.Ends).Duration() <= Tolerance
           && carried.Title == ChapterMetadataFile.TitleOf(wanted.Kind);

    private static IReadOnlyList<CarriedChapter>? Of(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty("chapters", out JsonElement chapters))
        {
            return [];
        }

        if (chapters.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        CarriedChapter?[] read = [.. chapters.EnumerateArray().Select(Chapter)];

        return read.Any(chapter => chapter is null) ? null : [.. read.OfType<CarriedChapter>()];
    }

    private static CarriedChapter? Chapter(JsonElement chapter)
    {
        if (Seconds(chapter, "start_time") is not { } starts || Seconds(chapter, "end_time") is not { } ends)
        {
            return null;
        }

        string title = chapter.TryGetProperty("tags", out JsonElement tags)
                       && tags.TryGetProperty("title", out JsonElement named)
                       && named.ValueKind is JsonValueKind.String
            ? named.GetString() ?? string.Empty
            : string.Empty;

        return new CarriedChapter(starts, ends, title);
    }

    private static TimeSpan? Seconds(JsonElement chapter, string name)
        => chapter.TryGetProperty(name, out JsonElement value)
           && value.ValueKind is JsonValueKind.String
           && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
           && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
}
