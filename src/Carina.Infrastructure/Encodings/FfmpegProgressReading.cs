using System.Globalization;

using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Reads what ffmpeg writes to <c>-progress pipe:1</c>, one line at a time, and hands back where
/// the job has got to at the end of each block. Every value is looked up by its key: reading by
/// position is what turned a whole programme into 1.4 seconds once already (BR-ED2-013).
/// <para>
/// <c>out_time_ms</c> is not read. Measured on ffmpeg 6.1.6 it holds microseconds, the same number
/// as <c>out_time_us</c>, so a reader that trusted its name would be a thousand times out.
/// </para>
/// </summary>
public sealed class FfmpegProgressReading(TimeSpan? whole)
{
    public const string ReachedKey = "out_time_us";

    public const string SpeedKey = "speed";

    public const string StandingKey = "progress";

    public const string Ended = "end";

    public const string Unknown = "N/A";

    private readonly Dictionary<string, string> block = new(StringComparer.Ordinal);

    public EncodeProgress? Read(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        string trimmed = line.Trim('\r', ' ', '\t');
        int equals = trimmed.IndexOf('=', StringComparison.Ordinal);

        if (equals < 1)
        {
            return null;
        }

        string key = trimmed[..equals];

        if (key.Contains(' ', StringComparison.Ordinal))
        {
            return null;
        }

        block[key] = trimmed[(equals + 1)..];

        return string.Equals(key, StandingKey, StringComparison.Ordinal) ? Finish() : null;
    }

    private EncodeProgress? Finish()
    {
        bool ended = string.Equals(Said(StandingKey), Ended, StringComparison.Ordinal);
        TimeSpan? reached = Reached();
        double speed = Speed();

        block.Clear();

        return reached is { } far ? EncodeProgress.Of(far, whole, speed, ended) : null;
    }

    private TimeSpan? Reached()
        => long.TryParse(Said(ReachedKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out long microseconds)
            && microseconds >= 0
            ? TimeSpan.FromMicroseconds(microseconds)
            : null;

    private double Speed()
    {
        string said = Said(SpeedKey).Trim().TrimEnd('x');

        return double.TryParse(said, NumberStyles.Float, CultureInfo.InvariantCulture, out double times)
            && times > 0
            ? times
            : 0;
    }

    private string Said(string key) => block.TryGetValue(key, out string? value) ? value : Unknown;
}
