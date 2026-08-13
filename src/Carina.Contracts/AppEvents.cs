namespace Carina.Contracts;

public static class AppEvents
{
    public const string Tuners = "tuners";

    public const string Programs = "programs";

    public const string EpgCollection = "epgCollection";

    public const string Reservations = "reservations";

    public const string Rules = "rules";

    public const string Recordings = "recordings";

    public const string Quality = "quality";

    public const string Live = "live";

    public const string EncodeJobs = "encodeJobs";

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

    public static bool IsKnown(string? name) =>
        name is not null && All.Contains(name, StringComparer.Ordinal);
}
