using Carina.Domain.Base;

namespace Carina.Domain.Quality;

public sealed class QualityIncident
{
    public const int ClassificationMaxLength = 64;

    public const int AcknowledgedByMaxLength = 128;

    private QualityIncident()
    {
    }

    public QualityIncidentId Id { get; private set; } = null!;

    public DateTime DetectedAt { get; private set; }

    public QualityThresholdKey Breached { get; private set; }

    public QualitySubject Subject { get; private set; } = null!;

    public double Observed { get; private set; }

    public QualityIncidentOwner Owner { get; private set; }

    public string? Classification { get; private set; }

    public Threshold Applied { get; private set; } = null!;

    public QualityIncidentState State { get; private set; }

    public DateTime? NotifiedAt { get; private set; }

    public DateTime? AcknowledgedAt { get; private set; }

    public string? AcknowledgedBy { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    public bool Restated => Owner is not QualityIncidentOwner.Quality;

    public bool HasSettled => State is QualityIncidentState.Resolved;

    public static QualityIncident Detect(
        QualityIncidentId id,
        DateTime detectedAt,
        QualityThresholdKey breached,
        QualitySubject subject,
        double observed,
        Threshold applied,
        QualityIncidentOwner owner = QualityIncidentOwner.Quality,
        string? classification = null)
        => Rehydrate(
            id,
            detectedAt,
            breached,
            subject,
            observed,
            owner,
            classification,
            applied,
            QualityIncidentState.Detected,
            null,
            null,
            null,
            null);

    public static QualityIncident Rehydrate(
        QualityIncidentId id,
        DateTime detectedAt,
        QualityThresholdKey breached,
        QualitySubject subject,
        double observed,
        QualityIncidentOwner owner,
        string? classification,
        Threshold applied,
        QualityIncidentState state,
        DateTime? notifiedAt,
        DateTime? acknowledgedAt,
        string? acknowledgedBy,
        DateTime? resolvedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(applied);
        Named(breached);
        Named(owner);
        Named(state);

        if ((owner is QualityIncidentOwner.Quality) != (classification is null))
        {
            throw new ArgumentException(
                "An anomaly another domain defines is kept under that domain's own classification, and one of this domain's own has none to keep.",
                nameof(classification));
        }

        if (classification is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(classification);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(classification.Length, ClassificationMaxLength, nameof(classification));
        }

        if (acknowledgedBy is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(acknowledgedBy);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(acknowledgedBy.Length, AcknowledgedByMaxLength, nameof(acknowledgedBy));
        }

        if ((acknowledgedAt is null) != (acknowledgedBy is null))
        {
            throw new ArgumentException("Being acknowledged means somebody acknowledged it.", nameof(acknowledgedBy));
        }

        UtcTimes.Required(detectedAt, nameof(detectedAt));
        UtcTimes.Optional(notifiedAt, nameof(notifiedAt));
        UtcTimes.Optional(acknowledgedAt, nameof(acknowledgedAt));
        UtcTimes.Optional(resolvedAt, nameof(resolvedAt));

        Ordered(notifiedAt, detectedAt, nameof(notifiedAt));
        Ordered(acknowledgedAt, notifiedAt, nameof(acknowledgedAt));
        Ordered(resolvedAt, detectedAt, nameof(resolvedAt));

        if (acknowledgedAt is not null && notifiedAt is null)
        {
            throw new ArgumentException("Nobody acknowledges an incident they were never told about.", nameof(acknowledgedAt));
        }

        if (state != Standing(notifiedAt, acknowledgedAt, resolvedAt))
        {
            throw new ArgumentException("An incident stands where its own times put it.", nameof(state));
        }

        return new QualityIncident
        {
            Id = id,
            DetectedAt = detectedAt,
            Breached = breached,
            Subject = subject,
            Observed = observed,
            Owner = owner,
            Classification = classification,
            Applied = applied,
            State = state,
            NotifiedAt = notifiedAt,
            AcknowledgedAt = acknowledgedAt,
            AcknowledgedBy = acknowledgedBy,
            ResolvedAt = resolvedAt,
        };
    }

    public void Notify(DateTime at)
    {
        Refuse(QualityIncidentState.Detected, "told about");
        NotifiedAt = Following(at, DetectedAt, nameof(at));
        State = QualityIncidentState.Notified;
    }

    public void Acknowledge(DateTime at, string by)
    {
        Refuse(QualityIncidentState.Notified, "acknowledged");
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(by.Length, AcknowledgedByMaxLength, nameof(by));

        AcknowledgedAt = Following(at, NotifiedAt!.Value, nameof(at));
        AcknowledgedBy = by;
        State = QualityIncidentState.Acknowledged;
    }

    public void Resolve(DateTime at)
    {
        if (HasSettled)
        {
            throw new InvalidOperationException(
                "An incident that has been resolved stays resolved, and the same condition coming back is a new one.");
        }

        ResolvedAt = Following(at, AcknowledgedAt ?? NotifiedAt ?? DetectedAt, nameof(at));
        State = QualityIncidentState.Resolved;
    }

    private static QualityIncidentState Standing(DateTime? notifiedAt, DateTime? acknowledgedAt, DateTime? resolvedAt)
        => resolvedAt is not null ? QualityIncidentState.Resolved
            : acknowledgedAt is not null ? QualityIncidentState.Acknowledged
            : notifiedAt is not null ? QualityIncidentState.Notified
            : QualityIncidentState.Detected;

    private static void Named<T>(T value)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"An incident carries a {typeof(T).Name} this domain knows.");
        }
    }

    private static void Ordered(DateTime? later, DateTime? earlier, string parameterName)
    {
        if (later is { } after && earlier is { } before && after < before)
        {
            throw new ArgumentException("An incident's own times only ever read forwards.", parameterName);
        }
    }

    private static DateTime Following(DateTime at, DateTime earlier, string parameterName)
    {
        UtcTimes.Required(at, parameterName);

        if (at < earlier)
        {
            throw new ArgumentException("An incident's own times only ever read forwards.", parameterName);
        }

        return at;
    }

    private void Refuse(QualityIncidentState expected, string what)
    {
        if (State != expected)
        {
            throw new InvalidOperationException($"An incident standing at {State} is not one to be {what}.");
        }
    }
}
