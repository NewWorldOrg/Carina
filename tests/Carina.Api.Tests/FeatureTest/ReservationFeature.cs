using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Tests.Unit;
using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class HeldReservationLedger : IReservationRepository
{
    private readonly List<Reservation> held = [];

    private readonly HashSet<Guid> recorded = [];

    public IReadOnlyList<Reservation> Held => held;

    public List<string> Wrote { get; } = [];

    public Exception? RefusesToAdd { get; set; }

    public Task<PaginatedList<Reservation>> ListAsync(
        ReservationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<Reservation> found = held;

        if (query.Standings.Count > 0)
        {
            found = found.Where(reservation => query.Standings.Contains(reservation.Standing));
        }

        if (query.Origin is { } origin)
        {
            found = found.Where(reservation =>
                reservation.IsRuleBorn == (origin is ReservationOrigin.ByRule));
        }

        if (query.Channels.Count > 0)
        {
            found = found.Where(reservation => query.Channels.Any(channel =>
                channel.NetworkId == reservation.NetworkId.Value
                && channel.ServiceId == reservation.ServiceId.Value));
        }

        if (query.Keyword is { } keyword)
        {
            found = found.Where(reservation =>
                reservation.SnapshotName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || reservation.SnapshotSummary.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        if (query.From is { } from)
        {
            found = found.Where(reservation => reservation.StartAt >= from);
        }

        if (query.To is { } to)
        {
            found = found.Where(reservation => reservation.StartAt < to);
        }

        Reservation[] matched = [.. found];
        Reservation[] ordered = query.Sort is ReservationSort.Priority
            ? [.. matched.OrderBy(reservation => reservation.Priority.Value).ThenBy(reservation => reservation.Id.Value)]
            : [.. matched.OrderBy(reservation => reservation.StartAt).ThenBy(reservation => reservation.Id.Value)];

        if (query.Descending)
        {
            ordered = query.Sort is ReservationSort.Priority
                ? [.. matched.OrderByDescending(reservation => reservation.Priority.Value).ThenBy(reservation => reservation.Id.Value)]
                : [.. matched.OrderByDescending(reservation => reservation.StartAt).ThenBy(reservation => reservation.Id.Value)];
        }

        return Task.FromResult(new PaginatedList<Reservation>(
            [.. ordered.Skip((query.Page - 1) * query.PerPage).Take(query.PerPage)],
            ordered.Length,
            query.Page,
            query.PerPage));
    }

    public Task<Reservation?> FindAsync(ReservationId id, CancellationToken cancellationToken)
        => Task.FromResult(held.FirstOrDefault(reservation => reservation.Id.Equals(id)));

    public Task<Reservation?> FindByProgrammeAsync(ProgrammeRef programme, CancellationToken cancellationToken)
        => Task.FromResult(held.FirstOrDefault(reservation => reservation.Programme.Equals(programme)));

    public Task<IReadOnlyList<Reservation>> ListPendingAsync(
        ReservationWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        return Task.FromResult<IReadOnlyList<Reservation>>(
        [
            .. held
                .Where(reservation => reservation.RecordingOutcome is null)
                .Where(reservation => reservation.State
                    is ReservationState.Scheduled or ReservationState.Conflict)
                .Where(reservation => reservation.IsPinned
                                      || (reservation.EndAt >= window.From && reservation.StartAt <= window.To))
                .OrderBy(reservation => reservation.StartAt)
                .ThenBy(reservation => reservation.Id.Value),
        ]);
    }

    public Task<IReadOnlyList<Reservation>> ListForRuleAsync(RuleId ruleId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Reservation>>(
            [.. held.Where(reservation => ruleId.Equals(reservation.RuleId))]);

    public Task<IReadOnlyList<Reservation>> ListForBroadcastGroupAsync(
        BroadcastGroupKey key,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Reservation>>(
            [.. held.Where(reservation => key.Equals(reservation.BroadcastGroupKey))]);

    public Task AddAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        if (RefusesToAdd is { } refusal)
        {
            held.Add(reservation);

            throw refusal;
        }

        Wrote.Add($"add {reservation.Id.Value}");
        held.Add(reservation);

        return Task.CompletedTask;
    }

    public Task SaveAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        Wrote.Add($"save {reservation.Id.Value}");

        return Task.CompletedTask;
    }

    public Task SaveAllAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservations);

        foreach (Reservation reservation in reservations)
        {
            Wrote.Add($"save {reservation.Id.Value}");
        }

        return Task.CompletedTask;
    }

    public Task<ReservationDiscard> DiscardAsync(
        ReservationId id,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (held.FirstOrDefault(reservation => reservation.Id.Equals(id)) is not { } standing)
        {
            return Task.FromResult(ReservationDiscard.NoSuchReservation);
        }

        if (recorded.Contains(id.Value))
        {
            return Task.FromResult(ReservationDiscard.RecordingCameOfIt);
        }

        if (standing.IsPinned && standing.RecordingOutcome is null)
        {
            return Task.FromResult(ReservationDiscard.TurningIntoARecording);
        }

        if (standing.State is ReservationState.Scheduled or ReservationState.Conflict
            && standing.RecordingOutcome is null
            && standing.EffectiveEndAt > at)
        {
            return Task.FromResult(ReservationDiscard.StillToBeRecorded);
        }

        Wrote.Add($"discard {id.Value}");
        held.Remove(standing);

        return Task.FromResult(ReservationDiscard.Discarded);
    }

    public void RecordingCameOf(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        recorded.Add(reservation.Id.Value);
    }

    public void RecordingThrownAwayFrom(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        recorded.Remove(reservation.Id.Value);
    }

    public Task WithdrawAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservations);

        foreach (Reservation reservation in reservations)
        {
            Wrote.Add($"withdraw {reservation.Id.Value}");
            held.Remove(reservation);
        }

        return Task.CompletedTask;
    }

    public void Standing(params Reservation[] reservations) => held.AddRange(reservations);
}

internal sealed class SeatsOnHand(TunerCapacity? capacity) : ITunerCapacityDirectory
{
    public TunerCapacity? Capacity { get; set; } = capacity;

    public Task<TunerCapacity?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Capacity);
}

internal sealed class TuningByServiceId : IServiceTuningDirectory
{
    private readonly Dictionary<int, TuningResolution> answers = [];

    public TuningResolution Otherwise { get; set; } = TuningResolution.Refused(TuningRefusal.NoSelectedChannel);

    public void Answer(int serviceId, TuningParameters tuning)
        => answers[serviceId] = TuningResolution.Tunable(
            new CandidateChannelId(Guid.NewGuid()),
            tuning,
            impaired: false);

    public void Refuse(int serviceId, TuningRefusal refusal)
        => answers[serviceId] = TuningResolution.Refused(refusal);

    public Task<TuningResolution> ResolveTuningAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
        => Task.FromResult(answers.TryGetValue(serviceId.Value, out TuningResolution? held) ? held : Otherwise);

    public Task<bool> CanTuneAsync(NetworkId networkId, ServiceId serviceId, CancellationToken cancellationToken)
        => Task.FromResult(
            (answers.TryGetValue(serviceId.Value, out TuningResolution? held) ? held : Otherwise).CanTune);
}

internal sealed class ReservationFeature : IAsyncDisposable
{
    public const int Network = 32736;

    public static readonly DateTime Noon = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public ReservationFeature(int seats = 1)
    {
        Seating = new SeatsOnHand(Terrestrial(seats));

        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IReservationRepository>(Reservations);
                services.AddSingleton<IProgrammeRepository>(Programmes);
                services.AddSingleton<ITunerCapacityDirectory>(Seating);
                services.AddSingleton<IServiceTuningDirectory>(Tuning);
                services.AddSingleton<IAtomicWrite>(new UnguardedWrites());
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Noon));
            }));

        Client = configured.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");

        Tuning.Answer(1024, TuningParameters.Terrestrial(27));
        Tuning.Answer(1032, TuningParameters.Terrestrial(29));
        Tuning.Answer(1040, TuningParameters.Terrestrial(31));
    }

    public HttpClient Client { get; }

    public HeldReservationLedger Reservations { get; } = new();

    public HeldProgrammes Programmes { get; } = new();

    public SeatsOnHand Seating { get; }

    public TuningByServiceId Tuning { get; } = new();

    public static TunerCapacity Terrestrial(int seats)
        => new(
            [
                .. Enumerable.Range(0, seats).Select(index =>
                    new TunerSeat($"seat{index}", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)),
            ],
            []);

    public Programme Announced(
        int eventId,
        int serviceId = 1024,
        DateTime? startsAt = null,
        DateTime? endsAt = null,
        bool endAnnounced = true,
        bool shadow = false,
        string name = "A programme",
        string summary = "What it is about")
    {
        DateTime opens = startsAt ?? Noon.AddHours(2);
        Programme programme = Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(serviceId), new EventId(eventId)),
            new TransportStreamId(32736),
            opens,
            endAnnounced ? endsAt ?? opens.AddHours(1) : null,
            name,
            summary,
            shadow,
            Noon,
            [new ProgrammeGenre(7, 1)],
            [new ProgrammeItem("Cast", "Somebody")],
            [],
            false,
            ProgrammeSource.ScheduleBasic);

        Programmes.Programmes.Add(programme);

        return programme;
    }

    public Reservation Booked(
        int eventId,
        int serviceId = 1024,
        DateTime? startsAt = null,
        ReservationState state = ReservationState.Scheduled,
        DateTime? startedAt = null,
        RecordingOutcome? outcome = null,
        int priority = Priority.DefaultValue,
        RuleId? ruleId = null,
        string name = "A programme",
        string summary = "What it is about")
    {
        DateTime opens = startsAt ?? Noon.AddHours(2);
        Reservation reservation = Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(Network), new ServiceId(serviceId), new EventId(eventId), opens),
            ruleId,
            new Priority(priority),
            opens,
            opens.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot(name, summary, string.Empty, [], Noon),
            null,
            BroadcastGroupRole.Standalone,
            state,
            startedAt,
            outcome,
            false,
            [],
            false,
            null,
            false,
            null,
            Noon);

        Reservations.Standing(reservation);

        return reservation;
    }

    public Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative)));

    public Task<(HttpStatusCode Status, JsonElement Body)> DeleteAsync(string path)
        => SendAsync(new HttpRequestMessage(HttpMethod.Delete, new Uri(path, UriKind.Relative)));

    public async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path, object? body = null)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            new Uri(path, UriKind.Relative),
            body ?? new { });

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PatchAsync(string path, object body)
    {
        using HttpResponseMessage response = await Client.PatchAsJsonAsync(
            new Uri(path, UriKind.Relative),
            body);

        return await ReadAsync(response);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpRequestMessage asking)
    {
        using (asking)
        {
            using HttpResponseMessage response = await Client.SendAsync(asking);

            return await ReadAsync(response);
        }
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        if (!body.StartsWith('{') && !body.StartsWith('['))
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }
}
