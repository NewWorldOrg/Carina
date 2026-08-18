namespace Carina.Domain.Programmes;

public sealed class Programme
{
    public const int NameMaxLength = 512;

    public const int SummaryMaxLength = 4096;

    private Programme()
    {
    }

    public ProgrammeId Id { get; private set; } = null!;

    public int TransportStreamId { get; private set; }

    public DateTime StartsAt { get; private set; }

    public DateTime? EndsAt { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public bool IsShadow { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Programme Discover(ProgrammeBroadcast broadcast, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(broadcast);

        return new Programme
        {
            Id = broadcast.Id,
            TransportStreamId = broadcast.TransportStreamId,
            StartsAt = broadcast.StartsAt,
            EndsAt = broadcast.EndsAt,
            Name = Clamped(broadcast.Name, NameMaxLength),
            Summary = Clamped(broadcast.Summary, SummaryMaxLength),
            IsShadow = broadcast.IsShadow,
            UpdatedAt = at,
        };
    }

    public static Programme Rehydrate(
        ProgrammeId id,
        int transportStreamId,
        DateTime startsAt,
        DateTime? endsAt,
        string name,
        string summary,
        bool isShadow,
        DateTime updatedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(summary);

        return new Programme
        {
            Id = id,
            TransportStreamId = transportStreamId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Name = name,
            Summary = summary,
            IsShadow = isShadow,
            UpdatedAt = updatedAt,
        };
    }

    public bool Absorb(ProgrammeBroadcast broadcast, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(broadcast);

        if (!Id.Equals(broadcast.Id))
        {
            throw new ArgumentException("That broadcast describes another programme.", nameof(broadcast));
        }

        var name = Kept(Name, Clamped(broadcast.Name, NameMaxLength));
        var summary = Kept(Summary, Clamped(broadcast.Summary, SummaryMaxLength));
        var endsAt = broadcast.EndsAt ?? EndsAt;

        if (TransportStreamId == broadcast.TransportStreamId
            && StartsAt == broadcast.StartsAt
            && EndsAt == endsAt
            && Name == name
            && Summary == summary
            && IsShadow == broadcast.IsShadow)
        {
            return false;
        }

        TransportStreamId = broadcast.TransportStreamId;
        StartsAt = broadcast.StartsAt;
        EndsAt = endsAt;
        Name = name;
        Summary = summary;
        IsShadow = broadcast.IsShadow;
        UpdatedAt = at;

        return true;
    }

    private static string Kept(string held, string arriving) => arriving.Length == 0 ? held : arriving;

    private static string Clamped(string text, int most) => text.Length <= most ? text : text[..most];
}
