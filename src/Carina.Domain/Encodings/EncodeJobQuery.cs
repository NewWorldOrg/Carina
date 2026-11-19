using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public sealed class EncodeJobQuery
{
    public const int MostPerPage = 100;

    public const int DefaultPerPage = 20;

    private EncodeJobQuery(IReadOnlyList<EncodeJobStatus> statuses, RecordingId? recording, int page, int perPage)
    {
        Statuses = statuses;
        Recording = recording;
        Page = page;
        PerPage = perPage;
    }

    public IReadOnlyList<EncodeJobStatus> Statuses { get; }

    public RecordingId? Recording { get; }

    public int Page { get; }

    public int PerPage { get; }

    public static EncodeJobQuery? For(
        IReadOnlyList<EncodeJobStatus>? statuses,
        RecordingId? recording,
        int? page,
        int? perPage)
    {
        if (page is < 1)
        {
            return null;
        }

        EncodeJobStatus[] asked = [.. (statuses ?? []).Distinct()];

        if (asked.Any(status => !Enum.IsDefined(status)))
        {
            return null;
        }

        return new EncodeJobQuery(
            asked,
            recording,
            page ?? 1,
            ListingGuards.Clamped(perPage, DefaultPerPage, MostPerPage));
    }
}
