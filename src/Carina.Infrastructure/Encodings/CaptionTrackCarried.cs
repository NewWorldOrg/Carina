using System.Globalization;
using System.Text.Json;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// One subtitle track a file carries: its codec, its language and whether it is shown before anybody
/// chooses it.
/// </summary>
public sealed record CarriedSubtitle(string Codec, string? Language, bool ShownByDefault);

/// <summary>
/// What a file carries as ffprobe reads it: how many picture and sound tracks, its subtitle tracks and how
/// long it lasts.
/// </summary>
public sealed record CaptionTrackCarried(int Pictures, int Sounds, IReadOnlyList<CarriedSubtitle> Subtitles, TimeSpan? Length)
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Reads what ffprobe said in JSON, or answers null when it said something else.
    /// </summary>
    public static CaptionTrackCarried? Read(string said)
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

    /// <summary>
    /// Why <paramref name="made"/> is not <paramref name="source"/> with one text track of captions put in, or
    /// null when it is: the same picture and sound tracks, exactly one subtitle track, written as text in
    /// Japanese and not shown by default, and as long as the source within <see cref="Tolerance"/>.
    /// </summary>
    public static string? Differs(CaptionTrackCarried source, CaptionTrackCarried made)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(made);

        if (made.Pictures != source.Pictures || made.Sounds != source.Sounds)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"it carries {made.Pictures} picture and {made.Sounds} sound track(s) where the artefact carries {source.Pictures} and {source.Sounds}");
        }

        if (made.Subtitles is not [CarriedSubtitle subtitle])
        {
            return string.Create(CultureInfo.InvariantCulture, $"it carries {made.Subtitles.Count} subtitle track(s), not one");
        }

        if (subtitle.Codec != FfmpegCaptionTrackInvocation.Codec
            || subtitle.Language != FfmpegCaptionTrackInvocation.Language
            || subtitle.ShownByDefault)
        {
            return $"its subtitle track is {subtitle.Codec} in '{subtitle.Language}', shown by default: {subtitle.ShownByDefault}";
        }

        if (source.Length is not { } was || made.Length is not { } now || (now - was).Duration() > Tolerance)
        {
            return $"it lasts {made.Length?.ToString() ?? "an unread time"} where the artefact lasts {source.Length?.ToString() ?? "an unread time"}";
        }

        return null;
    }

    private static CaptionTrackCarried? Of(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty("streams", out JsonElement streams)
            || streams.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        JsonElement[] all = [.. streams.EnumerateArray()];

        return new CaptionTrackCarried(
            all.Count(stream => Text(stream, "codec_type") is "video"),
            all.Count(stream => Text(stream, "codec_type") is "audio"),
            [.. all.Where(stream => Text(stream, "codec_type") is "subtitle").Select(Subtitle)],
            LengthOf(root));
    }

    private static CarriedSubtitle Subtitle(JsonElement stream)
        => new(
            Text(stream, "codec_name") ?? string.Empty,
            stream.TryGetProperty("tags", out JsonElement tags) ? Text(tags, "language") : null,
            stream.TryGetProperty("disposition", out JsonElement disposition)
                && disposition.TryGetProperty("default", out JsonElement shown)
                && shown.ValueKind is JsonValueKind.Number
                && shown.GetInt32() is not 0);

    private static TimeSpan? LengthOf(JsonElement root)
        => root.TryGetProperty("format", out JsonElement format)
           && double.TryParse(Text(format, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
           && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
}
