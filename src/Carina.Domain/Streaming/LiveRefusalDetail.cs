using Carina.Domain.Channels;

namespace Carina.Domain.Streaming;

public enum LiveTunerHolder
{
    ARecording = 1,

    AnotherViewer = 2,
}

public sealed class LiveRefusalDetail
{
    public static LiveRefusalDetail Unsaid { get; } = new(null, null);

    private LiveRefusalDetail(TuneFailureKind? tuneFailure, LiveTunerHolder? holder)
    {
        TuneFailure = tuneFailure;
        Holder = holder;
    }

    public TuneFailureKind? TuneFailure { get; }

    public LiveTunerHolder? Holder { get; }

    public byte Said => (byte)((int?)TuneFailure ?? (int?)Holder ?? 0);

    public static LiveRefusalDetail Of(TuneFailureKind tuneFailure)
    {
        if (!Enum.IsDefined(tuneFailure))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tuneFailure),
                tuneFailure,
                "A tuning fails in one of the four ways named here.");
        }

        return new LiveRefusalDetail(tuneFailure, null);
    }

    public static LiveRefusalDetail Of(LiveTunerHolder holder)
    {
        if (!Enum.IsDefined(holder))
        {
            throw new ArgumentOutOfRangeException(
                nameof(holder),
                holder,
                "A tuner is held by one of the holders named here.");
        }

        return new LiveRefusalDetail(null, holder);
    }

    public bool Fits(LiveRefusal refusal)
        => (TuneFailure is null || refusal is LiveRefusal.WouldNotTune)
           && (Holder is null || refusal is LiveRefusal.NoTunerFree);

    public static LiveRefusalDetail? Read(LiveRefusal refusal, byte said)
        => said switch
        {
            0 => Unsaid,
            _ when refusal is LiveRefusal.WouldNotTune && Enum.IsDefined((TuneFailureKind)said)
                => new LiveRefusalDetail((TuneFailureKind)said, null),
            _ when refusal is LiveRefusal.NoTunerFree && Enum.IsDefined((LiveTunerHolder)said)
                => new LiveRefusalDetail(null, (LiveTunerHolder)said),
            _ => null,
        };
}
