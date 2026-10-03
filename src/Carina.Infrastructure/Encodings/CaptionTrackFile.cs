using System.Globalization;
using System.Text;

using Carina.Domain.Captions;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// The text of a recording's captions written as WebVTT on an artefact's clock: each text shown is a cue
/// from its moment less the job's caption shift until the next change. A cue that begins before zero begins
/// at zero, one that ends before zero or begins at or after the end is dropped, one that runs past the end
/// is cut there, and the last one still shown lasts to the end, or <see cref="LastCueWithNoEnd"/> when no
/// end is known.
/// </summary>
public static class CaptionTrackFile
{
    public static readonly TimeSpan LastCueWithNoEnd = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The file, or null when no cue is left to write.
    /// </summary>
    public static string? Written(IReadOnlyList<CaptionLine> lines, TimeSpan shift, TimeSpan? length)
    {
        ArgumentNullException.ThrowIfNull(lines);

        StringBuilder written = new("WEBVTT\n");
        int cues = 0;

        for (int at = 0; at < lines.Count; at++)
        {
            if (lines[at].Text is not { } text)
            {
                continue;
            }

            TimeSpan from = lines[at].At - shift;
            TimeSpan until = at + 1 < lines.Count ? lines[at + 1].At - shift : length ?? from + LastCueWithNoEnd;

            if (Cue(from, until, length) is not { } cue)
            {
                continue;
            }

            written.Append('\n')
                .Append(Moment(cue.From))
                .Append(" --> ")
                .Append(Moment(cue.Until))
                .Append('\n')
                .Append(Escaped(text))
                .Append('\n');
            cues++;
        }

        return cues is 0 ? null : written.ToString();
    }

    private static (TimeSpan From, TimeSpan Until)? Cue(TimeSpan from, TimeSpan until, TimeSpan? length)
    {
        TimeSpan begins = from < TimeSpan.Zero ? TimeSpan.Zero : from;
        TimeSpan ends = length is { } end && until > end ? end : until;

        if (length is { } last && begins >= last)
        {
            return null;
        }

        return ends > begins ? (begins, ends) : null;
    }

    private static string Moment(TimeSpan at)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{(long)at.TotalHours:00}:{at.Minutes:00}:{at.Seconds:00}.{at.Milliseconds:000}");

    private static string Escaped(string text)
    {
        StringBuilder escaped = new(text.Length);

        foreach (char letter in text)
        {
            escaped.Append(Escape(letter));
        }

        return escaped.ToString();
    }

    private static string Escape(char letter)
        => letter switch
        {
            '&' => "&amp;",
            '<' => "&lt;",
            '>' => "&gt;",
            _ => letter.ToString(),
        };
}
