using System.Diagnostics.CodeAnalysis;

namespace Carina.Broadcast.Tables;

public static class BroadcastTime
{
    public const int StartSize = 5;

    public const int DurationSize = 3;

    private static readonly TimeSpan Offset = TimeSpan.FromHours(9);

    public static bool TryReadStart(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out DateTimeOffset? start)
    {
        start = null;

        if (bytes.Length < StartSize)
        {
            return false;
        }

        var days = (bytes[0] << 8) | bytes[1];

        if (!TryReadClock(bytes[2..StartSize], 23, out var clock))
        {
            return false;
        }

        var years = (int)((days - 15078.2) / 365.25);
        var months = (int)((days - 14956.1 - (int)(years * 365.25)) / 30.6001);
        var day = days - 14956 - (int)(years * 365.25) - (int)(months * 30.6001);
        var wrapped = months is 14 or 15 ? 1 : 0;
        var year = years + wrapped + 1900;
        var month = months - 1 - (wrapped * 12);

        if (month is < 1 or > 12 || day is < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        start = new DateTimeOffset(year, month, day, 0, 0, 0, Offset) + clock.Value;

        return true;
    }

    public static bool TryReadDuration(ReadOnlySpan<byte> bytes, out TimeSpan? duration)
    {
        duration = null;

        if (bytes.Length < DurationSize)
        {
            return false;
        }

        if (bytes[0] == 0xFF && bytes[1] == 0xFF && bytes[2] == 0xFF)
        {
            return true;
        }

        if (!TryReadClock(bytes[..DurationSize], 99, out var clock))
        {
            return false;
        }

        duration = clock;

        return true;
    }

    private static bool TryReadClock(ReadOnlySpan<byte> bytes, int mostHours, [NotNullWhen(true)] out TimeSpan? clock)
    {
        clock = null;

        if (!TryReadDecimal(bytes[0], out var hours)
            || !TryReadDecimal(bytes[1], out var minutes)
            || !TryReadDecimal(bytes[2], out var seconds))
        {
            return false;
        }

        if (hours > mostHours || minutes > 59 || seconds > 59)
        {
            return false;
        }

        clock = new TimeSpan(hours, minutes, seconds);

        return true;
    }

    private static bool TryReadDecimal(byte packed, out int value)
    {
        var tens = packed >> 4;
        var units = packed & 0x0F;

        value = (tens * 10) + units;

        return tens <= 9 && units <= 9;
    }
}
