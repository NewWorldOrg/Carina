namespace Carina.Contracts;

/// <summary>
/// Names carried by the app's single server-sent event stream.
/// </summary>
/// <remarks>
/// One hub, one event per thing that changed, and no payload: the browser learns
/// that something moved and re-reads it over REST. Putting the change itself in the
/// event would turn the event shape into an API, and the contract only grows —
/// names are never renamed or removed, so the shape could never be corrected.
/// Collections are plural; an activity is singular.
/// </remarks>
public static class AppEvents
{
    /// <summary>Tuner inventory, detection results and health.</summary>
    public const string Tuners = "tuners";

    /// <summary>Programme data.</summary>
    public const string Programs = "programs";

    /// <summary>Progress of a collection run.</summary>
    public const string EpgCollection = "epgCollection";

    /// <summary>Reservations.</summary>
    public const string Reservations = "reservations";

    /// <summary>Recording rules.</summary>
    public const string Rules = "rules";

    /// <summary>Recording sessions and their outcomes.</summary>
    public const string Recordings = "recordings";

    /// <summary>Quality aggregates and anomalies.</summary>
    public const string Quality = "quality";

    /// <summary>Live viewing sessions.</summary>
    public const string Live = "live";

    /// <summary>Encode jobs.</summary>
    public const string EncodeJobs = "encodeJobs";

    /// <summary>Every name the stream may carry, in domain order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Tuners,
        Programs,
        EpgCollection,
        Reservations,
        Rules,
        Recordings,
        Quality,
        Live,
        EncodeJobs,
    ];

    /// <summary>Whether <paramref name="name"/> belongs to the agreed set.</summary>
    public static bool IsKnown(string? name) =>
        name is not null && All.Contains(name, StringComparer.Ordinal);
}
