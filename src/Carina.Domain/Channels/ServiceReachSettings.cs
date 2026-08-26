using Carina.Domain.Base;

namespace Carina.Domain.Channels;

public sealed class ServiceReachSettings
{
    public const int TheOnlyRow = 1;

    public const int DefaultHoursOfSilence = 24;

    public const int ShortestHoursOfSilence = 1;

    public const int LongestHoursOfSilence = 720;

    private ServiceReachSettings()
    {
    }

    public int Id { get; private set; }

    public int HoursOfSilence { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public TimeSpan Silence => TimeSpan.FromHours(HoursOfSilence);

    public static ServiceReachSettings Default(DateTime at) =>
        Rehydrate(TheOnlyRow, DefaultHoursOfSilence, at);

    public static ServiceReachSettings Rehydrate(int id, int hoursOfSilence, DateTime updatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hoursOfSilence, ShortestHoursOfSilence, nameof(hoursOfSilence));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hoursOfSilence, LongestHoursOfSilence, nameof(hoursOfSilence));

        return new ServiceReachSettings
        {
            Id = id,
            HoursOfSilence = hoursOfSilence,
            UpdatedAt = UtcTimes.Required(updatedAt, nameof(updatedAt)),
        };
    }

    public void AllowSilenceFor(int hoursOfSilence, DateTime at)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hoursOfSilence, ShortestHoursOfSilence, nameof(hoursOfSilence));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hoursOfSilence, LongestHoursOfSilence, nameof(hoursOfSilence));

        HoursOfSilence = hoursOfSilence;
        UpdatedAt = UtcTimes.Required(at, nameof(at));
    }
}
