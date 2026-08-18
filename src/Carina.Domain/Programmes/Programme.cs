using Carina.Domain.Base;
using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public sealed class Programme
{
    public const int NameMaxLength = 512;

    public const int SummaryMaxLength = 4096;

    private Programme()
    {
    }

    public ProgrammeId Id { get; private set; } = null!;

    public TransportStreamId TransportStreamId { get; private set; } = null!;

    public DateTime StartsAt { get; private set; }

    public DateTime? EndsAt { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public bool IsShadow { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Programme Discover(ProgrammeBroadcast broadcast, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(broadcast);

        return Rehydrate(
            broadcast.Id,
            broadcast.TransportStreamId,
            broadcast.StartsAt,
            broadcast.EndsAt,
            broadcast.Name,
            broadcast.Summary,
            broadcast.IsShadow,
            at);
    }

    public static Programme Rehydrate(
        ProgrammeId id,
        TransportStreamId transportStreamId,
        DateTime startsAt,
        DateTime? endsAt,
        string name,
        string summary,
        bool isShadow,
        DateTime updatedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(transportStreamId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(summary);

        return new Programme
        {
            Id = id,
            TransportStreamId = transportStreamId,
            StartsAt = UtcTimes.Required(startsAt, nameof(startsAt)),
            EndsAt = Settled(startsAt, UtcTimes.Optional(endsAt, nameof(endsAt))),
            Name = Clamped(name, NameMaxLength),
            Summary = Clamped(summary, SummaryMaxLength),
            IsShadow = isShadow,
            UpdatedAt = UtcTimes.Required(updatedAt, nameof(updatedAt)),
        };
    }

    public bool Absorb(ProgrammeBroadcast broadcast, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(broadcast);
        UtcTimes.Required(at, nameof(at));

        if (!Id.Equals(broadcast.Id))
        {
            throw new ArgumentException("That broadcast describes another programme.", nameof(broadcast));
        }

        var startsAt = UtcTimes.Required(broadcast.StartsAt, nameof(broadcast));
        var name = Kept(Name, Clamped(broadcast.Name, NameMaxLength));
        var summary = Kept(Summary, Clamped(broadcast.Summary, SummaryMaxLength));
        var endsAt = Settled(startsAt, UtcTimes.Optional(broadcast.EndsAt, nameof(broadcast)) ?? EndsAt);

        if (TransportStreamId.Equals(broadcast.TransportStreamId)
            && StartsAt == startsAt
            && EndsAt == endsAt
            && Name == name
            && Summary == summary
            && IsShadow == broadcast.IsShadow)
        {
            return false;
        }

        TransportStreamId = broadcast.TransportStreamId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Name = name;
        Summary = summary;
        IsShadow = broadcast.IsShadow;
        UpdatedAt = at;

        return true;
    }

    private static DateTime? Settled(DateTime startsAt, DateTime? endsAt)
        => endsAt is { } ends && ends > startsAt ? ends : null;

    private static string Kept(string held, string arriving) => arriving.Length == 0 ? held : arriving;

    private static string Clamped(string text, int most) => text.Length <= most ? text : text[..most];
}
