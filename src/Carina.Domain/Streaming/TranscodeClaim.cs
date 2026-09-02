namespace Carina.Domain.Streaming;

public enum TranscodePurpose
{
    Live = 1,

    Playback = 2,
}

public sealed record TranscodeCeiling
{
    public TranscodeCeiling(int running, int atOnce)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(atOnce, TranscodeBudgetSettings.Fewest);
        ArgumentOutOfRangeException.ThrowIfLessThan(running, atOnce);

        Running = running;
        AtOnce = atOnce;
    }

    public int Running { get; }

    public int AtOnce { get; }

    public string Said
        => $"{Running} transcoder(s) are already running, live and playback together, which is as many at once as this machine is asked to ({AtOnce}).";
}

public sealed class TranscodeClaim
{
    private TranscodeClaim(ITranscodeSeat? seat, TranscodeCeiling? refusal)
    {
        Seat = seat;
        Refusal = refusal;
    }

    public ITranscodeSeat? Seat { get; }

    public TranscodeCeiling? Refusal { get; }

    public bool Taken => Seat is not null;

    public static TranscodeClaim Seated(ITranscodeSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);

        return new TranscodeClaim(seat, null);
    }

    public static TranscodeClaim Refused(TranscodeCeiling ceiling)
    {
        ArgumentNullException.ThrowIfNull(ceiling);

        return new TranscodeClaim(null, ceiling);
    }
}
