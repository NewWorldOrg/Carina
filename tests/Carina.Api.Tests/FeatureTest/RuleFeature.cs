using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Tests.Unit;
using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Rules;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class HeldRuleLedger : IRuleRepository
{
    public List<Rule> Rules { get; } = [];

    public List<string> Wrote { get; } = [];

    public Task<Rule?> FindAsync(RuleId id, CancellationToken cancellationToken)
        => Task.FromResult(Rules.FirstOrDefault(rule => rule.Id.Equals(id)));

    public Task<IReadOnlyList<Rule>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult(RuleMatcher.InPrecedence(Rules));

    public Task<IReadOnlyList<Rule>> ListEnabledByPrecedenceAsync(CancellationToken cancellationToken)
        => Task.FromResult(RuleMatcher.InPrecedence(Rules.Where(rule => rule.Enabled)));

    public Task AddAsync(Rule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);

        Wrote.Add($"add {rule.Id.Value}");
        Rules.Add(rule);

        return Task.CompletedTask;
    }

    public Task SaveAsync(Rule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);

        Wrote.Add($"save {rule.Id.Value}");

        return Task.CompletedTask;
    }

    public Task RemoveAsync(Rule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);

        Wrote.Add($"remove {rule.Id.Value}");
        Rules.RemoveAll(held => held.Id.Equals(rule.Id));

        return Task.CompletedTask;
    }
}

internal sealed class AnsweredPasses : IRecalculationPass
{
    private readonly TaskCompletionSource entered = new();

    private readonly List<RecalculationTrigger> asked = [];

    public int Ran { get; private set; }

    public TaskCompletionSource? Held { get; set; }

    public Task Entered => entered.Task;

    public RecalculationPass? Answers { get; set; }

    public RuleApplicationRun? Applied { get; set; } = new(11, 0, [], [], [], [], []);

    public IReadOnlyList<RecalculationTrigger> Asked
    {
        get
        {
            lock (asked)
            {
                return [.. asked];
            }
        }
    }

    public async Task<RecalculationPass> RunAsync(
        RecalculationTrigger asking,
        CancellationToken cancellationToken)
    {
        lock (this)
        {
            Ran++;
        }

        lock (asked)
        {
            asked.Add(asking);
        }

        entered.TrySetResult();

        if (Held is { } gate)
        {
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        return Answers ?? RecalculationPass.Of(
            [asking],
            RecalculationReaches.Of(asking),
            11,
            Applied,
            null,
            null,
            []);
    }
}

internal sealed class RuleFeature : IAsyncDisposable
{
    public const int Network = 32736;

    public const int Carried = 32736;

    public const int Listed = 1024;

    public const int Alongside = 1032;

    public static readonly DateTime Noon = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public RuleFeature(int seats = 2)
    {
        Seating = new SeatsOnHand(ReservationFeature.Terrestrial(seats));
        Streams = new HeldStreams(
        [
            new BroadcastStream(
                new NetworkId(Network),
                new TransportStreamId(Carried),
                TuningParameters.Terrestrial(27),
                [new ServiceId(Listed), new ServiceId(Alongside)]),
        ]);

        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IRuleRepository>(Rules);
                services.AddSingleton<IReservationRepository>(Reservations);
                services.AddSingleton<IProgrammeRepository>(Programmes);
                services.AddSingleton<IStreamVisitRepository>(Visits);
                services.AddSingleton<IBroadcastStreamDirectory>(Streams);
                services.AddSingleton<IBroadcastServiceRepository>(Services);
                services.AddSingleton<ITunerCapacityDirectory>(Seating);
                services.AddSingleton<IServiceTuningDirectory>(Tuning);
                services.AddSingleton<IAtomicWrite>(new UnguardedWrites());
                services.AddSingleton<IRecalculationNotice>(Notices);
                services.AddSingleton<IAppEventPublisher>(Events);
                services.AddSingleton<IRecalculationPass>(Passes);
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Noon));
            }));

        Client = configured.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");

        Tuning.Answer(Listed, TuningParameters.Terrestrial(27));
        Tuning.Answer(Alongside, TuningParameters.Terrestrial(29));
    }

    public HttpClient Client { get; }

    public HeldRuleLedger Rules { get; } = new();

    public HeldReservationLedger Reservations { get; } = new();

    public HeldProgrammes Programmes { get; } = new();

    public HeldStreamVisits Visits { get; } = new();

    public HeldStreams Streams { get; }

    public HeldServices Services { get; } = new();

    public SeatsOnHand Seating { get; }

    public TuningByServiceId Tuning { get; } = new();

    public CountedNotices Notices { get; } = new();

    public SilentEvents Events { get; } = new();

    public AnsweredPasses Passes { get; } = new();

    public Rule Written(
        string query = "keyword=hill",
        string name = "a rule",
        int priority = Priority.DefaultValue,
        bool enabled = true,
        int identifier = 1)
    {
        Rule rule = Rule.Draft(
            new RuleId(new Guid($"{identifier:x8}-0000-0000-0000-000000000000")),
            name,
            new RuleQuery(query),
            new Priority(priority),
            enabled,
            Margin.None,
            Margin.None,
            Noon.AddDays(-30));

        Rules.Rules.Add(rule);

        return rule;
    }

    public Programme Announced(
        int eventId,
        string name = "hill walking",
        int serviceId = Listed,
        DateTime? startsAt = null,
        bool shadow = false,
        int transportStreamId = Carried)
    {
        DateTime opens = startsAt ?? Noon.AddHours(2 + eventId);
        Programme programme = Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(serviceId), new EventId(eventId)),
            new TransportStreamId(transportStreamId),
            opens,
            opens.AddHours(1),
            name,
            "what it is about",
            shadow,
            Noon,
            [new ProgrammeGenre(7, 1)],
            [],
            [],
            false,
            ProgrammeSource.ScheduleBasic,
            revision: eventId);

        Programmes.Programmes.Add(programme);

        return programme;
    }

    public Reservation Booked(Programme programme, RuleId? ruleId = null)
    {
        ArgumentNullException.ThrowIfNull(programme);

        Reservation reservation = Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt),
            ruleId,
            Priority.Default,
            programme.StartsAt,
            programme.StartsAt.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot(programme.Name, programme.Summary, string.Empty, [], Noon),
            null,
            BroadcastGroupRole.Standalone,
            ReservationState.Scheduled,
            null,
            null,
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

    public void Collected(int transportStreamId = Carried, VisitOutcome outcome = VisitOutcome.Complete)
        => Visits.Visits.Add(StreamVisit.Record(
            new NetworkId(Network),
            new TransportStreamId(transportStreamId),
            outcome,
            Noon.AddHours(-1),
            TimeSpan.FromSeconds(30)));

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

    public async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(string path, object body)
    {
        using HttpResponseMessage response = await Client.PutAsJsonAsync(new Uri(path, UriKind.Relative), body);

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PatchAsync(string path, object body)
    {
        using HttpResponseMessage response = await Client.PatchAsJsonAsync(new Uri(path, UriKind.Relative), body);

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
