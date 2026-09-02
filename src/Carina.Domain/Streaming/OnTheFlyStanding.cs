namespace Carina.Domain.Streaming;

public sealed record OnTheFlyStanding
{
    public OnTheFlyStanding(
        TimeSpan startsAt,
        TimeSpan waited,
        LiveProfile profile,
        LiveEncoderChoice encoder,
        bool attributesWereMeasured,
        int running,
        int atOnce)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentOutOfRangeException.ThrowIfLessThan(startsAt, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(waited, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(atOnce, TranscodeBudgetSettings.Fewest);
        ArgumentOutOfRangeException.ThrowIfLessThan(running, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(running, atOnce);

        StartsAt = startsAt;
        Waited = waited;
        Profile = profile;
        Encoder = encoder;
        AttributesWereMeasured = attributesWereMeasured;
        Running = running;
        AtOnce = atOnce;
    }

    public TimeSpan StartsAt { get; }

    public TimeSpan Waited { get; }

    public LiveProfile Profile { get; }

    public LiveEncoderChoice Encoder { get; }

    public bool AttributesWereMeasured { get; }

    public int Running { get; }

    public int AtOnce { get; }
}
