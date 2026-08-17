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
    private readonly IReadOnlyList<string> notImplementedMetrics = [];
    private readonly IReadOnlyList<string> metricsOnAnotherScale = [];

    public SignalQualityDto() { }

    private SignalQualityDto(SignalQualityDto other)
    {
        Lock = other.Lock;
        cnrMilliDecibels = other.CnrMilliDecibels;
        postViterbiBitErrors = other.PostViterbiBitErrors;
        MeasuredAt = other.MeasuredAt;
        LockReadAt = other.LockReadAt;
        notImplementedMetrics = other.NotImplementedMetrics;
        metricsOnAnotherScale = other.MetricsOnAnotherScale;
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

    public DateTimeOffset? LockReadAt { get; init; }

    public IReadOnlyList<string> NotImplementedMetrics
    {
        get => notImplementedMetrics;
        init => notImplementedMetrics = value ?? [];
    }

    public IReadOnlyList<string> MetricsOnAnotherScale
    {
        get => metricsOnAnotherScale;
        init => metricsOnAnotherScale = value ?? [];
    }

    [JsonIgnore]
    public decimal? CnrDecibels => CnrMilliDecibels / 1000m;

    public bool Implements(string metric) =>
        !NotImplementedMetrics.Contains(metric, StringComparer.Ordinal);

    public static SignalQualityDto NotLocked(DateTimeOffset? measuredAt = null) =>
        new() { Lock = SignalLock.NotLocked, MeasuredAt = measuredAt };

    public bool Equals(SignalQualityDto? other) =>
        other is not null
        && Lock == other.Lock
        && CnrMilliDecibels == other.CnrMilliDecibels
        && MeasuredAt == other.MeasuredAt
        && LockReadAt == other.LockReadAt
        && PostViterbiBitErrors.SequenceEqual(other.PostViterbiBitErrors)
        && NotImplementedMetrics.SequenceEqual(other.NotImplementedMetrics, StringComparer.Ordinal)
        && MetricsOnAnotherScale.SequenceEqual(other.MetricsOnAnotherScale, StringComparer.Ordinal);

    public override int GetHashCode() =>
        HashCode.Combine(
            Lock,
            CnrMilliDecibels,
            MeasuredAt,
            LockReadAt,
            PostViterbiBitErrors.Count,
            NotImplementedMetrics.Count,
            MetricsOnAnotherScale.Count
        );
}

public static class SignalQualityMetrics
{
    public const string Cnr = "cnr";

    public const string PostViterbiBitError = "postViterbiBitError";

    public static readonly IReadOnlyList<string> All = [Cnr, PostViterbiBitError];
}
