namespace Carina.Domain.Channels;

public enum TuneFailureKind
{
    NoLock = 1,

    NoData = 2,

    IncompletePsi = 3,

    StreamMismatch = 4,
}
