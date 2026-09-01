using System.Globalization;
using System.Text.Json;

using Carina.Domain.Playback;
using Carina.Domain.Streaming;

namespace Carina.Api.Playback;

public static class PlaybackHeaders
{
    public const string Standing = "Carina-Playback-Standing";

    public const string Route = "Carina-Playback-Route";

    public const string Seeking = "Carina-Playback-Seeking";

    public const string CanSeek = "Carina-Playback-Can-Seek";

    public const string StartsAt = "Carina-Playback-Starts-At";

    public const string Waited = "Carina-Playback-Waited";

    public const string Profile = "Carina-Playback-Profile";

    public const string Encoder = "Carina-Playback-Encoder";

    public const string Running = "Carina-Playback-Running";

    public const string AtOnce = "Carina-Playback-At-Once";

    public const string AttributesWereMeasured = "Carina-Playback-Attributes-Measured";

    public const string Refusal = "Carina-Playback-Refusal";

    public static readonly IReadOnlyList<string> Every =
    [
        Standing,
        Route,
        Seeking,
        CanSeek,
        StartsAt,
        Waited,
        Profile,
        Encoder,
        Running,
        AtOnce,
        AttributesWereMeasured,
        Refusal,
    ];

    public static void Say(HttpResponse response, PlaybackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(plan);

        response.Headers[Standing] = Named(plan.Standing);
        response.Headers[Route] = Named(plan.Route);
        response.Headers[CanSeek] = Word(plan.Seeking is PlaybackSeeking.ByRange);

        if (plan.Seeking is { } seeking)
        {
            response.Headers[Seeking] = Named(seeking);
        }
    }

    public static void Say(HttpResponse response, OnTheFlyStanding standing)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(standing);

        response.Headers[StartsAt] = Number(standing.StartsAt.TotalSeconds);
        response.Headers[Waited] = Number(standing.Waited.TotalSeconds);
        response.Headers[Profile] = standing.Profile.Name;
        response.Headers[Encoder] = Named(standing.Encoder.Encoder);
        response.Headers[Running] = Number(standing.Running);
        response.Headers[AtOnce] = Number(standing.AtOnce);
        response.Headers[AttributesWereMeasured] = Word(standing.AttributesWereMeasured);
    }

    public static void SayItStartsAtTheBeginning(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Headers[StartsAt] = Number(0);
    }

    public static void SayWhyNot(HttpResponse response, OnTheFlyRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Headers[Refusal] = Named(refusal);
    }

    private static string Named<T>(T value)
        where T : struct, Enum
        => JsonNamingPolicy.CamelCase.ConvertName(value.ToString()!);

    private static string Word(bool told) => told ? "true" : "false";

    private static string Number(double measured)
        => measured.ToString("0.###", CultureInfo.InvariantCulture);
}
