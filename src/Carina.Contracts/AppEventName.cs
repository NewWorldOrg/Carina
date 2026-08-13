namespace Carina.Contracts;

public sealed class AppEventName
{
    public static readonly AppEventName Tuners = new(AppEvents.Tuners);

    public static readonly AppEventName Programs = new(AppEvents.Programs);

    public static readonly AppEventName EpgCollection = new(AppEvents.EpgCollection);

    public static readonly AppEventName Reservations = new(AppEvents.Reservations);

    public static readonly AppEventName Rules = new(AppEvents.Rules);

    public static readonly AppEventName Recordings = new(AppEvents.Recordings);

    public static readonly AppEventName Quality = new(AppEvents.Quality);

    public static readonly AppEventName Live = new(AppEvents.Live);

    public static readonly AppEventName EncodeJobs = new(AppEvents.EncodeJobs);

    public static readonly IReadOnlyList<AppEventName> All =
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

    private AppEventName(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;
}
