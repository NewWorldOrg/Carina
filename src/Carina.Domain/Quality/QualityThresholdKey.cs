namespace Carina.Domain.Quality;

public enum QualityThresholdKey
{
    PacketsLostWarning = 1,

    PacketsLostUnwatchable = 2,

    PacketsLeftScrambled = 3,

    Overflows = 4,

    LockRate = 5,

    CarrierToNoiseFloor = 6,

    BitErrorRateCeiling = 7,

    SupplySilence = 8,
}
