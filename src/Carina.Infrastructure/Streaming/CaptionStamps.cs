using System.Globalization;
using System.Text.RegularExpressions;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed record CaptionStamp(int Index, LivePts? Pts);

public sealed partial class CaptionStamps
{
    private uint numerator = 1;

    private uint denominator = LivePts.Hertz;

    public CaptionStamp? Read(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (!line.StartsWith("[Parsed_showinfo_", StringComparison.Ordinal))
        {
            return null;
        }

        Match clock = TimeBase().Match(line);

        if (clock.Success)
        {
            uint said = uint.Parse(clock.Groups["den"].ValueSpan, CultureInfo.InvariantCulture);

            if (said > 0)
            {
                numerator = uint.Parse(clock.Groups["num"].ValueSpan, CultureInfo.InvariantCulture);
                denominator = said;
            }

            return null;
        }

        Match stamp = Stamp().Match(line);

        if (!stamp.Success)
        {
            return null;
        }

        int index = int.Parse(stamp.Groups["n"].ValueSpan, CultureInfo.InvariantCulture);

        return ulong.TryParse(stamp.Groups["pts"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out ulong ticks)
            ? new CaptionStamp(index, LivePts.Rescaled(ticks * numerator, denominator))
            : new CaptionStamp(index, null);
    }

    [GeneratedRegex(@"\] config in time_base: (?<num>\d+)/(?<den>\d+)")]
    private static partial Regex TimeBase();

    [GeneratedRegex(@"\] n:\s*(?<n>\d+) pts:\s*(?<pts>\S+) ")]
    private static partial Regex Stamp();
}
