using System.Text.Json.Serialization;

namespace Carina.Contracts;

[JsonConverter(typeof(SignalLockConverter))]
public enum SignalLock
{
    Unspecified = 0,

    NotLocked = 1,

    Locked = 2,
}

public sealed record LayerBitErrorCounts(int Layer, long ErrorBits, long TotalBits)
{
    [JsonIgnore]
    public double? ErrorRate => TotalBits > 0 ? (double)ErrorBits / TotalBits : null;
}

public sealed record SignalQualityDto
{
    private readonly int? cnrMilliDecibels;
    private readonly IReadOnlyList<LayerBitErrorCounts> postViterbiBitErrors = [];

    public SignalQualityDto() { }

    private SignalQualityDto(SignalQualityDto other)
    {
        Lock = other.Lock;
        cnrMilliDecibels = other.CnrMilliDecibels;
        postViterbiBitErrors = other.PostViterbiBitErrors;
        MeasuredAt = other.MeasuredAt;
    }

    public SignalLock Lock { get; init; }

    public int? CnrMilliDecibels
    {
        get => Lock is SignalLock.Locked ? cnrMilliDecibels : null;
        init => cnrMilliDecibels = value;
    }

    public IReadOnlyList<LayerBitErrorCounts> PostViterbiBitErrors
    {
        get => Lock is SignalLock.Locked ? postViterbiBitErrors : [];
        init => postViterbiBitErrors = value ?? [];
    }

    public DateTimeOffset? MeasuredAt { get; init; }

    [JsonIgnore]
    public decimal? CnrDecibels => CnrMilliDecibels / 1000m;

    public static SignalQualityDto NotLocked(DateTimeOffset? measuredAt = null) =>
        new() { Lock = SignalLock.NotLocked, MeasuredAt = measuredAt };

    public bool Equals(SignalQualityDto? other) =>
        other is not null
        && Lock == other.Lock
        && CnrMilliDecibels == other.CnrMilliDecibels
        && MeasuredAt == other.MeasuredAt
        && PostViterbiBitErrors.SequenceEqual(other.PostViterbiBitErrors);

    public override int GetHashCode() =>
        HashCode.Combine(Lock, CnrMilliDecibels, MeasuredAt, PostViterbiBitErrors.Count);
}

public static class SignalQualityMetrics
{
    public const string Cnr = "cnr";

    public const string PostViterbiBitError = "postViterbiBitError";

    public static readonly IReadOnlyList<string> All = [Cnr, PostViterbiBitError];
}
