namespace Carina.Infrastructure.Streaming;

public enum LiveTrack
{
    Picture = 1,

    Sound = 2,
}

public enum LiveFragmentKind
{
    Initialisation = 1,

    Media = 2,
}

public enum LiveFragmentFault
{
    NotTheContainerItWasAskedFor = 1,

    ASizeNoBoxCanHave = 2,

    ABoxWithoutAnEnd = 3,

    ABoxTooBigToHold = 4,

    MediaBeforeItsHeader = 5,

    StoppedPartWayThrough = 6,
}

public sealed record LiveFragment(LiveTrack Track, LiveFragmentKind Kind, ReadOnlyMemory<byte> Bytes);

public sealed record LiveFragmenting(IReadOnlyList<LiveFragment> Fragments, LiveFragmentFault? Fault)
{
    public static readonly LiveFragmenting Nothing = new([], null);

    public bool Broke => Fault is not null;
}
