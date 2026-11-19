using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Unit.Segments;

internal sealed class CatalogSeries(int station, string name, DateTime firstAiring, TimeSpan? runs = null)
{
    public static readonly TimeSpan Lead = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan Runs = TimeSpan.FromMinutes(30);

    public static readonly double Midpoint = (Runs / 2).TotalSeconds;

    public static readonly DateTime Now = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    public ProgrammeCopy Episode(int week)
    {
        DateTime starts = firstAiring.AddDays(7 * week);

        return new ProgrammeCopy(
            new NetworkId(station),
            new ServiceId(station),
            starts,
            starts + (runs ?? Runs),
            starts - Lead,
            $"{name} #{week + 1}",
            [],
            [],
            week + 1,
            AudioMode.Stereo,
            name);
    }

    public static PlacedOccurrence At(
        CatalogEntryId entry,
        ProgrammeCopy programme,
        double fromProgrammeStart,
        double seconds,
        RecordingId? recording = null)
    {
        TimeSpan starts = Lead + TimeSpan.FromSeconds(fromProgrammeStart);
        SoundSpan span = new(starts, starts + TimeSpan.FromSeconds(seconds));
        CatalogOccurrence occurrence = CatalogOccurrence.Found(entry, recording ?? RecordingId.New(), span, programme, 0.05, Now);

        return PlacedOccurrence.Of(occurrence, programme);
    }
}
