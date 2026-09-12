using Carina.Domain.Streaming;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveDepartureLedger : ILiveDepartureLedger
{
    private static readonly LiveDeparture[] Ways = Enum.GetValues<LiveDeparture>();

    private readonly Lock gate = new();

    private readonly long[] times = new long[Ways.Length];

    private readonly DateTime?[] lastAt = new DateTime?[Ways.Length];

    private readonly TimeSpan?[] shortest = new TimeSpan?[Ways.Length];

    private readonly TimeSpan?[] longest = new TimeSpan?[Ways.Length];

    private readonly TimeProvider clock;

    private readonly ILogger<LiveDepartureLedger> logger;

    private readonly DateTime since;

    public LiveDepartureLedger(TimeProvider clock, ILogger<LiveDepartureLedger> logger)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.clock = clock;
        this.logger = logger;
        since = clock.GetUtcNow().UtcDateTime;
    }

    public void Note(LiveSessionKey key, LiveDeparture departure, TimeSpan carried)
    {
        ArgumentNullException.ThrowIfNull(key);

        int at = Array.IndexOf(Ways, departure);

        if (at < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(departure),
                departure,
                "A wire ends in one of the ways named here.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(carried, TimeSpan.Zero);

        lock (gate)
        {
            times[at]++;
            lastAt[at] = clock.GetUtcNow().UtcDateTime;
            shortest[at] = shortest[at] is { } least && least < carried ? least : carried;
            longest[at] = longest[at] is { } most && most > carried ? most : carried;
        }

        logger.LogInformation(
            "A live wire on {Key} ended as {Departure} after {CarriedSeconds} second(s).",
            key.ToString(),
            departure,
            Math.Round(carried.TotalSeconds, 1));
    }

    public LiveDepartureTally Read()
    {
        lock (gate)
        {
            return new LiveDepartureTally(
                since,
                [
                    .. Ways.Select((way, at) => new LiveDepartureCount(
                        way,
                        times[at],
                        lastAt[at],
                        shortest[at],
                        longest[at])),
                ]);
        }
    }
}
