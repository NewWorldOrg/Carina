using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Programmes;

[Trait("Category", "DbIntegration")]
public sealed class ProgrammeRepositoryTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private static readonly DateTime At = new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Later = At.AddHours(1);

    private static readonly Dictionary<string, Visit> Visits = new(StringComparer.Ordinal)
    {
        ["a programme nobody held yet"] = new(_ => [], network => [Broadcast(network)]),
        ["the same broadcast again"] = new(network => [Broadcast(network)], network => [Broadcast(network)]),
        ["a name that arrives empty"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Name = string.Empty }]),
        ["a summary that arrives empty"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Summary = string.Empty }]),
        ["a name that arrives empty for a programme whose name was already empty"] = new(
            network => [Broadcast(network) with { Name = string.Empty, IsShadow = true }],
            network => [Broadcast(network) with { Name = string.Empty, IsShadow = true }]),
        ["a name for a programme that had none"] = new(
            network => [Broadcast(network) with { Name = string.Empty, IsShadow = true }],
            network => [Broadcast(network)]),
        ["an end not yet known"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { EndsAt = null }]),
        ["an end arriving for one that was open"] = new(
            network => [Broadcast(network) with { EndsAt = null }],
            network => [Broadcast(network)]),
        ["an end that is not after the start"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { EndsAt = At.AddHours(22) }]),
        ["an end that is not after the start, for one nobody held"] = new(
            _ => [],
            network => [Broadcast(network) with { EndsAt = At.AddHours(22) }]),
        ["a start moved past the end, with no end of its own"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { StartsAt = At.AddHours(23), EndsAt = null }]),
        ["a start moved past the end, ending where it starts"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { StartsAt = At.AddHours(23), EndsAt = At.AddHours(23) }]),
        ["a start that moved but not past the end"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { StartsAt = At.AddHours(22.5), EndsAt = null }]),
        ["a later end"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { EndsAt = At.AddHours(24) }]),
        ["genres that arrive empty"] = new(
            network => [Broadcast(network) with { Genres = [new ProgrammeGenre(0, 15)] }],
            network => [Broadcast(network)]),
        ["genres that arrive different"] = new(
            network => [Broadcast(network) with { Genres = [new ProgrammeGenre(0, 15)] }],
            network => [Broadcast(network) with { Genres = [new ProgrammeGenre(7, 2), new ProgrammeGenre(0, 15)] }]),
        ["genres that arrive in another order"] = new(
            network => [Broadcast(network) with { Genres = [new ProgrammeGenre(0, 15), new ProgrammeGenre(7, 2)] }],
            network => [Broadcast(network) with { Genres = [new ProgrammeGenre(7, 2), new ProgrammeGenre(0, 15)] }]),
        ["items that arrive empty"] = new(
            network => [Broadcast(network) with { Items = [new ProgrammeItem("番組内容", "きょうの内容")] }],
            network => [Broadcast(network)]),
        ["items that arrive for one that had none"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Items = [new ProgrammeItem("番組内容", "きょうの内容")] }]),
        ["items whose text changed"] = new(
            network => [Broadcast(network) with { Items = [new ProgrammeItem("番組内容", "きょうの内容")] }],
            network => [Broadcast(network) with { Items = [new ProgrammeItem("番組内容", "あしたの内容")] }]),
        ["related programmes that arrive empty"] = new(
            network => [Broadcast(network) with { Related = [new RelatedProgramme(network, 1048, 1, RelationKind.Shared)] }],
            network => [Broadcast(network)]),
        ["related programmes that arrive different"] = new(
            network => [Broadcast(network) with { Related = [new RelatedProgramme(network, 1048, 1, RelationKind.Shared)] }],
            network => [Broadcast(network) with { Related = [new RelatedProgramme(network, 1048, 1, RelationKind.Moved)] }]),
        ["another stream"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { TransportStreamId = new TransportStreamId(32740) }]),
        ["a placeholder that became a programme"] = new(
            network => [Broadcast(network) with { Name = string.Empty, IsShadow = true }],
            network => [Broadcast(network) with { IsShadow = false }]),
        ["a programme that became a placeholder"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Name = string.Empty, IsShadow = true }]),
        ["subtitles that appeared"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { HasSubtitles = true }]),
        ["subtitles that went away"] = new(
            network => [Broadcast(network) with { HasSubtitles = true }],
            network => [Broadcast(network)]),
        ["a picture that arrives undetermined for one already known"] = new(
            network => [Broadcast(network) with { Video = VideoMode.Interlaced1080, Aspect = AspectRatio.SixteenByNine }],
            network => [Broadcast(network)]),
        ["a picture for a programme that had none"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Video = VideoMode.Progressive720, Aspect = AspectRatio.SixteenByNine }]),
        ["a sound that arrives undetermined for one already known"] = new(
            network => [Broadcast(network) with { Audio = AudioMode.Stereo, Sounds = 2 }],
            network => [Broadcast(network)]),
        ["a sound for a programme that had none"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Audio = AudioMode.DualMono, Sounds = 1 }]),
        ["a running status that arrives undetermined for one already known"] = new(
            network => [Broadcast(network) with { Running = ProgrammeRunning.Running }],
            network => [Broadcast(network)]),
        ["a running status that changed"] = new(
            network => [Broadcast(network) with { Running = ProgrammeRunning.NotRunning }],
            network => [Broadcast(network) with { Running = ProgrammeRunning.Running }]),
        ["a running status for a programme that had none"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Running = ProgrammeRunning.Pausing }]),
        ["a source that changed"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Source = ProgrammeSource.PresentFollowing }]),
        ["a detailed table after the basic one"] = new(
            network => [Broadcast(network) with { HasSubtitles = true }],
            network =>
            [
                Broadcast(network) with
                {
                    Name = string.Empty,
                    Summary = string.Empty,
                    Items = [new ProgrammeItem("番組内容", "きょうの内容")],
                    Source = ProgrammeSource.ScheduleExtended,
                },
            ]),
        ["a detailed table for one nobody held"] = new(
            _ => [],
            network => [Broadcast(network) with { Name = string.Empty, Source = ProgrammeSource.ScheduleExtended }]),
        ["the basic table after a detailed one"] = new(
            network => [Broadcast(network) with { Name = string.Empty, Source = ProgrammeSource.ScheduleExtended }],
            network => [Broadcast(network) with { HasSubtitles = true }]),
        ["a name longer than the column"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Name = new string('長', Programme.NameMaxLength + 40) }]),
        ["a name exactly as long as the column, already held"] = new(
            network => [Broadcast(network) with { Name = new string('長', Programme.NameMaxLength) }],
            network => [Broadcast(network) with { Name = new string('長', Programme.NameMaxLength + 1) }]),
        ["a summary longer than the column"] = new(
            _ => [],
            network => [Broadcast(network) with { Summary = new string('概', Programme.SummaryMaxLength + 1) }]),
        ["a name with a character outside the basic plane"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Name = "\U0001F211字幕つき\U0001F214" }]),
        ["several programmes, some new, some changed, some the same"] = new(
            network => [Broadcast(network, 1), Broadcast(network, 2), Broadcast(network, 3) with { EndsAt = null }],
            network =>
            [
                Broadcast(network, 1),
                Broadcast(network, 2) with { Name = "夜のニュース" },
                Broadcast(network, 3),
                Broadcast(network, 4),
                Broadcast(network, 5) with { Name = string.Empty, IsShadow = true },
            ]),
    };

    private readonly ServiceProvider container = database.Infrastructure();

    public static TheoryData<string> WhatArrives()
    {
        var carried = new TheoryData<string>();

        foreach (string named in Visits.Keys)
        {
            carried.Add(named);
        }

        return carried;
    }

    [Fact]
    public async Task AProgrammeComesBackWithEverythingItWasBroadcastWith()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Programme.Discover(Broadcast(network) with
        {
            Genres = [new ProgrammeGenre(0, 15), new ProgrammeGenre(11, 5)],
            Items = [new ProgrammeItem("番組内容", "きょうの内容")],
            Related = [new RelatedProgramme(network, 1048, 47289, RelationKind.Shared)],
            HasSubtitles = true,
            Source = ProgrammeSource.PresentFollowing,
        }, At), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Programme? stored = await ProgrammesIn(reading).FindAsync(Id(network), Cancel);

        Assert.NotNull(stored);
        Assert.Equal("トップニュース先出し🈑", stored.Name);
        Assert.Equal([0, 11], stored.Genres.Select(genre => genre.Kind));
        Assert.Equal("番組内容", Assert.Single(stored.Items).Heading);
        Assert.Equal(RelationKind.Shared, Assert.Single(stored.Related).Kind);
        Assert.True(stored.HasSubtitles);
        Assert.Equal(ProgrammeSource.PresentFollowing, stored.Source);
        Assert.Equal(At.AddHours(23), stored.EndsAt);
    }

    [Fact]
    public async Task AProgrammeWhoseEndIsStillOpenComesBackWithoutOne()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ProgrammesIn(scope).AddAsync(
            Programme.Discover(Broadcast(network) with { EndsAt = null }, At),
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Null((await ProgrammesIn(reading).FindAsync(Id(network), Cancel))!.EndsAt);
    }

    [Fact]
    public async Task OnlyTheProgrammesTouchingTheWindowComeBack()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Programme.Discover(Broadcast(network, 1, At.AddHours(20)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 2, At.AddHours(20.5)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 3, At.AddHours(22)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 4, At.AddHours(24)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 5, At.AddHours(30)), At), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        IReadOnlyList<Programme> carried = await ProgrammesIn(reading).ListAsync(
            new ProgrammeWindow(network, 1049, At.AddHours(21), At.AddHours(24)),
            Cancel);

        Assert.Equal([2, 3], carried.Select(programme => programme.EventId.Value));
    }

    [Fact]
    public async Task AProgrammeWhoseEndIsStillOpenIsCarriedByAnyWindowAfterItStarts()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ProgrammesIn(scope).AddAsync(
            Programme.Discover(Broadcast(network, 1, At.AddHours(22)) with { EndsAt = null }, At),
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        IReadOnlyList<Programme> carried = await ProgrammesIn(reading).ListAsync(
            new ProgrammeWindow(network, 1049, At.AddHours(23), At.AddHours(24)),
            Cancel);

        Assert.Single(carried);
    }

    [Fact]
    public async Task OnlyTheProgrammesHandedOverAreForgotten()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Programme.Discover(Broadcast(network, 1, At.AddHours(1)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 2, At.AddHours(40)), At), Cancel);

        Assert.Equal(1, await programmes.ForgetAsync(await Ended(programmes, network), Cancel));

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Null(await ProgrammesIn(reading).FindAsync(Id(network, 1), Cancel));
        Assert.NotNull(await ProgrammesIn(reading).FindAsync(Id(network, 2), Cancel));
    }

    [Fact]
    public async Task ForgettingNothingReadsNothingAndWritesNothing()
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        Assert.Equal(0, await ProgrammesIn(scope).ForgetAsync([], Cancel));
    }

    [Fact]
    public async Task WhatAProgrammeTookInLaterComesBackFromTheStore()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Programme.Discover(Broadcast(network), At), Cancel);

        ProgrammesAbsorbed absorbed = await programmes.AbsorbAsync(
            [
                Broadcast(network) with
                {
                    Genres = [new ProgrammeGenre(7, 2)],
                    Items = [new ProgrammeItem("公式ページ", "https://example.invalid/")],
                    HasSubtitles = true,
                },
            ],
            [],
            At.AddHours(1),
            Cancel);

        Assert.Equal(new ProgrammesAbsorbed(0, 1), absorbed);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Programme? stored = await ProgrammesIn(reading).FindAsync(Id(network), Cancel);

        Assert.Equal(new ProgrammeGenre(7, 2), Assert.Single(stored!.Genres));
        Assert.Equal("公式ページ", Assert.Single(stored.Items).Heading);
        Assert.True(stored.HasSubtitles);
        Assert.Equal(At.AddHours(1), stored.UpdatedAt);
    }

    [Fact]
    public async Task OneVisitIsWrittenAsOneStatementAndCountsWhatItAddedApartFromWhatItChanged()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Programme.Discover(Broadcast(network, 1), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 2), At), Cancel);

        ProgrammesAbsorbed absorbed = await programmes.AbsorbAsync(
            [
                Broadcast(network, 1),
                Broadcast(network, 2) with { Name = "夜のニュース" },
                Broadcast(network, 3),
                Broadcast(network, 4),
            ],
            [],
            At.AddHours(1),
            Cancel);

        Assert.Equal(new ProgrammesAbsorbed(2, 1), absorbed);
    }

    [Fact]
    public async Task BrEd002TheSameVisitArrivingAgainMovesNoRevisionAndNoClock()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AbsorbAsync([Broadcast(network)], [], At, Cancel);

        await using AsyncServiceScope first = container.CreateAsyncScope();
        Programme before = (await ProgrammesIn(first).FindAsync(Id(network), Cancel))!;

        Assert.Equal(new ProgrammesAbsorbed(0, 0), await programmes.AbsorbAsync([Broadcast(network)], [], At.AddHours(1), Cancel));

        await using AsyncServiceScope second = container.CreateAsyncScope();
        Programme after = (await ProgrammesIn(second).FindAsync(Id(network), Cancel))!;

        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(At, after.UpdatedAt);
    }

    [Fact]
    public async Task BrEd002AProgrammeThatChangedTakesARevisionAfterEveryOneHandedOutSoFar()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AbsorbAsync([Broadcast(network, 1), Broadcast(network, 2)], [], At, Cancel);

        await using AsyncServiceScope first = container.CreateAsyncScope();
        long highest = (await ProgrammesIn(first).ListAfterAsync(0, 5_000, Cancel)).Max(programme => programme.Revision);

        await programmes.AbsorbAsync([Broadcast(network, 1) with { Summary = "あとから届いた概要" }], [], At.AddHours(1), Cancel);

        await using AsyncServiceScope second = container.CreateAsyncScope();
        Programme changed = (await ProgrammesIn(second).FindAsync(Id(network, 1), Cancel))!;

        Assert.True(changed.Revision > highest);
    }

    [Fact]
    public async Task AReaderFollowingTheRevisionsMissesNoWriteThatCommittedLate()
    {
        int network = NextNetwork();
        await using AsyncServiceScope before = container.CreateAsyncScope();
        long cursor = (await ProgrammesIn(before).ListAfterAsync(0, int.MaxValue, Cancel))
            .Select(programme => programme.Revision)
            .DefaultIfEmpty(0)
            .Max();
        await using AsyncServiceScope slow = container.CreateAsyncScope();
        await using AsyncServiceScope quick = container.CreateAsyncScope();
        await using IDbContextTransaction held = await ContextIn(slow).Database.BeginTransactionAsync(Cancel);

        await ProgrammesIn(slow).AbsorbAsync([Broadcast(network, 1)], [], At, Cancel);

        Task<int> following = AtomicIn(quick).AllOrNothingAsync(
            async token => (await ProgrammesIn(quick).AbsorbAsync([Broadcast(network, 2)], [], At, token)).Added,
            Cancel);

        await UntilWaitingOrDoneAsync(following);

        List<Programme> read = [.. await ReadAfterAsync(cursor)];

        cursor = read.Select(programme => programme.Revision).DefaultIfEmpty(cursor).Max();
        await held.CommitAsync(Cancel);
        await following;
        read.AddRange(await ReadAfterAsync(cursor));

        Assert.Equal(
            [1, 2],
            read.Where(programme => programme.NetworkId.Value == network).Select(programme => programme.EventId.Value).Order());
    }

    [Fact]
    public async Task AVisitThatCarriesNothingWritesNothing()
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        Assert.Equal(new ProgrammesAbsorbed(0, 0), await ProgrammesIn(scope).AbsorbAsync([], [], At, Cancel));
    }

    [Fact]
    public async Task AVisitThatNamesTheSameProgrammeTwiceIsRefusedBeforeItReachesTheStore()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await Assert.ThrowsAsync<ArgumentException>(() => ProgrammesIn(scope).AbsorbAsync(
            [Broadcast(network), Broadcast(network) with { Name = "別の名前" }],
            [],
            At,
            Cancel));
    }

    [Fact]
    public async Task AbsorbingInsideAnAllOrNothingWriteIsUndoneWithIt()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicIn(scope).AllOrNothingAsync<int>(
            async token =>
            {
                await programmes.AbsorbAsync([Broadcast(network)], [], At, token);

                throw new InvalidOperationException("the rest of the visit failed");
            },
            Cancel));

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Null(await ProgrammesIn(reading).FindAsync(Id(network), Cancel));
    }

    [Fact]
    public async Task AProgrammeWhoseEndWasNeverToldHasNotEndedBeforeTheNextOneOnItsServiceBegins()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(
            Programme.Discover(Broadcast(network, 1, At.AddHours(1)) with { EndsAt = null }, At),
            Cancel);
        await programmes.AddAsync(
            Programme.Discover(Broadcast(network, 2, At.AddHours(40)) with { EndsAt = null }, At),
            Cancel);

        Assert.Empty(await Ended(programmes, network));

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.NotNull(await ProgrammesIn(reading).FindAsync(Id(network, 1), Cancel));
        Assert.NotNull(await ProgrammesIn(reading).FindAsync(Id(network, 2), Cancel));
    }

    [Fact]
    public async Task AProgrammeWhoseEndWasNeverToldEndedWhereTheNextOneOnItsServiceBegan()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(
            Programme.Discover(Broadcast(network, 1, At.AddHours(1)) with { EndsAt = null }, At),
            Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 2, At.AddHours(3)), At), Cancel);
        await programmes.AddAsync(
            Programme.Discover(Carried(network, 1050, 3, At.AddHours(2)) with { EndsAt = null }, At),
            Cancel);

        EndedProgramme[] ended =
        [
            .. (await programmes.ListEndedBeforeAsync(At.AddHours(10), 5_000, Cancel))
                .Where(programme => programme.Programme.NetworkId.Value == network),
        ];

        Assert.Equal([1, 2], ended.Select(programme => programme.Programme.EventId.Value));
        Assert.Equal([At.AddHours(3), At.AddHours(4)], ended.Select(programme => programme.EndedAt));
    }

    [Fact]
    public async Task HowFarAServiceIsCoveredCountsTheSlotsItOnlyShares()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Programme.Discover(Broadcast(network, 1, At.AddHours(20)), At), Cancel);
        await programmes.AddAsync(
            Programme.Discover(Broadcast(network, 2, At.AddHours(40)) with { IsShadow = true }, At),
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Equal(
            At.AddHours(41),
            await ProgrammesIn(reading).CoveredUntilAsync(network, 1049, Cancel));
    }

    [Fact]
    public async Task HowFarAServiceIsCoveredIsWhereItsLatestProgrammeEndsNotWhereTheLastOneStarts()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(
            Programme.Discover(Broadcast(network, 1, At.AddHours(20)) with { EndsAt = At.AddHours(26) }, At),
            Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 2, At.AddHours(22)), At), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Equal(
            At.AddHours(26),
            await ProgrammesIn(reading).CoveredUntilAsync(network, 1049, Cancel));
    }

    [Fact]
    public async Task AProgrammeWhoseEndIsUndecidedCoversAsFarAsItsStart()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Programme.Discover(Broadcast(network, 1, At.AddHours(20)), At), Cancel);
        await programmes.AddAsync(
            Programme.Discover(Broadcast(network, 2, At.AddHours(30)) with { EndsAt = null }, At),
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Equal(
            At.AddHours(30),
            await ProgrammesIn(reading).CoveredUntilAsync(network, 1049, Cancel));
    }

    [Fact]
    public async Task AServiceWithNoProgrammesIsCoveredUntilNoTimeAtAll()
    {
        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Null(await ProgrammesIn(reading).CoveredUntilAsync(NextNetwork(), 1049, Cancel));
    }

    [Fact]
    public async Task TheProgrammesOfEveryAskedServiceComeBackTogether()
    {
        int network = NextNetwork();
        int neighbour = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Programme.Discover(Carried(network, 1049, 1, At.AddHours(22)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Carried(neighbour, 1050, 1, At.AddHours(22.5)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Carried(network, 1049, 2, At.AddHours(30)), At), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<Programme> found = await ProgrammesIn(reading).ListForServicesAsync(
            [new ProgrammeService(network, 1049), new ProgrammeService(neighbour, 1050)],
            At.AddHours(21),
            At.AddHours(24),
            Cancel);

        Assert.Equal(
            [(network, 1049, 1), (neighbour, 1050, 1)],
            found.Select(programme
                => (programme.NetworkId.Value, programme.ServiceId.Value, programme.EventId.Value)));
    }

    [Fact]
    public async Task AProgrammeOfANetworkOrServiceNotAskedForIsNotCarried()
    {
        int network = NextNetwork();
        int neighbour = NextNetwork();
        int stranger = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Programme.Discover(Carried(network, 1049, 1, At.AddHours(22)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Carried(network, 1050, 2, At.AddHours(22)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Carried(network, 1048, 3, At.AddHours(22)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Carried(stranger, 1049, 4, At.AddHours(22)), At), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<Programme> found = await ProgrammesIn(reading).ListForServicesAsync(
            [new ProgrammeService(network, 1049), new ProgrammeService(neighbour, 1050)],
            At.AddHours(21),
            At.AddHours(24),
            Cancel);

        Assert.DoesNotContain(found, programme => programme.NetworkId.Value == stranger);
        Assert.DoesNotContain(found, programme => programme.ServiceId.Value == 1048);
        Assert.DoesNotContain(
            found,
            programme => programme.NetworkId.Value == network && programme.ServiceId.Value == 1050);
        Assert.Equal(1, Assert.Single(found).EventId.Value);
    }

    [Fact]
    public async Task ASkeletonEventIsHeldButNeverHandedToTheGuide()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Programme.Discover(Carried(network, 1049, 1, At.AddHours(22)), At), Cancel);
        await programmes.AddAsync(
            Programme.Discover(Carried(network, 1049, 2, At.AddHours(22)) with { IsShadow = true }, At),
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IProgrammeRepository asked = ProgrammesIn(reading);
        IReadOnlyList<Programme> found = await asked.ListForServicesAsync(
            [new ProgrammeService(network, 1049)],
            At.AddHours(21),
            At.AddHours(24),
            Cancel);

        Assert.Equal(1, Assert.Single(found).EventId.Value);
        Assert.NotNull(await asked.FindAsync(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(2)),
            Cancel));
    }

    [Fact]
    public async Task AReadingMarksTheProgrammesItNamedOnTheServicesItHeardWhole()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ProgrammesIn(scope).AbsorbAsync(
            [Broadcast(network, 1), Carried(network, 1050, 2, At.AddHours(22))],
            [new ProgrammeService(network, 1049)],
            At,
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IProgrammeRepository asked = ProgrammesIn(reading);

        Assert.Equal(At, (await asked.FindAsync(Id(network), Cancel))!.LastHeardAt);
        Assert.Null((await asked.FindAsync(
            new ProgrammeId(new NetworkId(network), new ServiceId(1050), new EventId(2)),
            Cancel))!.LastHeardAt);
    }

    [Fact]
    public async Task AProgrammeHeardAgainWithNothingChangedIsMarkedAgainAnyway()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);
        ProgrammeService[] whole = [new ProgrammeService(network, 1049)];

        await programmes.AbsorbAsync([Broadcast(network)], whole, At, Cancel);

        Assert.Equal(
            new ProgrammesAbsorbed(0, 0),
            await programmes.AbsorbAsync([Broadcast(network)], whole, At.AddHours(1), Cancel));

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Programme stored = (await ProgrammesIn(reading).FindAsync(Id(network), Cancel))!;

        Assert.Equal(At, stored.UpdatedAt);
        Assert.Equal(At.AddHours(1), stored.LastHeardAt);
    }

    [Fact]
    public async Task WhenAServiceWasLastHeardWholeIsTheNewestMarkAnyOfItsProgrammesCarries()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);
        ProgrammeService[] whole = [new ProgrammeService(network, 1049)];

        Assert.Null(await programmes.HeardWholeAtAsync(network, 1049, Cancel));

        await programmes.AbsorbAsync([Broadcast(network, 1), Broadcast(network, 2)], whole, At, Cancel);
        await programmes.AbsorbAsync([Broadcast(network, 2)], whole, At.AddHours(1), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IProgrammeRepository asked = ProgrammesIn(reading);

        Assert.Equal(At.AddHours(1), await asked.HeardWholeAtAsync(network, 1049, Cancel));
        Assert.Equal(At, (await asked.FindAsync(Id(network), Cancel))!.LastHeardAt);
    }

    [Fact]
    public async Task BrRd010TheGroupedProgrammesAreTheOnesCarryingARelayOrAMoveAndTheOnesTheyName()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Programme.Discover(Broadcast(network, 1) with
        {
            Related = [new RelatedProgramme(network, 1049, 2, RelationKind.Relayed)],
        }, At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 2), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 3) with
        {
            Related = [new RelatedProgramme(network, 1049, 4, RelationKind.Moved)],
        }, At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 4), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 5) with
        {
            Related = [new RelatedProgramme(network, 1049, 6, RelationKind.Shared)],
        }, At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 6), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 7), At), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<Programme> grouped = await ProgrammesIn(reading).ListGroupedAsync(Cancel);

        Assert.Equal(
            [1, 2, 3, 4],
            grouped
                .Where(programme => programme.NetworkId.Value == network)
                .Select(programme => programme.EventId.Value)
                .Order());
    }

    [Fact]
    public async Task BrRd010TheGroupedProgrammesTakeInTheSameBroadcastOnAnotherServiceSharedOrAlike()
    {
        int network = NextNetwork();
        DateTime noon = At.AddHours(22);
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        ProgrammeBroadcast[] carried =
        [
            Aired(network, 901, 1, noon, 60, "山歩き") with { Related = [new RelatedProgramme(network, 902, 1, RelationKind.Shared)] },
            Aired(network, 902, 1, noon, 60, "山歩き") with { Related = [new RelatedProgramme(network, 901, 1, RelationKind.Shared)] },
            Aired(network, 903, 1, noon, 60, string.Empty) with
            {
                IsShadow = true,
                Related = [new RelatedProgramme(network, 901, 1, RelationKind.Shared)],
            },
            Aired(network, 901, 2, noon.AddHours(1), 60, "川釣り"),
            Aired(network, 902, 3, noon.AddHours(1), 60, "川釣り"),
            Aired(network, 901, 4, noon.AddHours(2), 60, "海"),
            Aired(network, 902, 5, noon.AddHours(2), 60, "海", stream: 9003),
            Aired(network, 901, 6, noon.AddHours(3), 60, "島"),
            Aired(network, 902, 7, noon.AddHours(3), 45, "島"),
            Aired(network, 901, 8, noon.AddHours(4), 60, "空") with { Related = [new RelatedProgramme(network, 901, 9, RelationKind.Shared)] },
            Aired(network, 901, 9, noon.AddHours(5), 60, "空"),
            Aired(network, 901, 10, noon.AddHours(6), 60, "森") with { Related = [new RelatedProgramme(network, 902, 10, RelationKind.Shared)] },
            Aired(network, 902, 10, noon.AddHours(6), 60, "森の続き") with { Related = [new RelatedProgramme(network, 901, 10, RelationKind.Shared)] },
            Aired(network, 901, 11, noon.AddHours(7), 60, "谷") with { Related = [new RelatedProgramme(network, 902, 11, RelationKind.Shared)] },
            Aired(network, 902, 11, noon.AddHours(7), 45, "谷") with { Related = [new RelatedProgramme(network, 901, 11, RelationKind.Shared)] },
            Aired(network, 901, 12, noon.AddHours(8), 60, "湖") with { EndsAt = null },
            Aired(network, 902, 12, noon.AddHours(8), 60, "湖") with { EndsAt = null },
        ];

        foreach (ProgrammeBroadcast broadcast in carried)
        {
            await programmes.AddAsync(Programme.Discover(broadcast, At), Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<Programme> grouped = await ProgrammesIn(reading).ListGroupedAsync(Cancel);

        Assert.Equal(
            [(901, 1), (901, 2), (901, 10), (901, 11), (902, 1), (902, 3), (902, 10), (902, 11)],
            grouped
                .Where(programme => programme.NetworkId.Value == network)
                .Select(programme => (programme.ServiceId.Value, programme.EventId.Value))
                .Order());
    }

    [Fact]
    public async Task BrRd010AProgrammeNamedByALinkButNotInTheGuideIsLeftOutOfTheGroupedOnes()
    {
        int network = NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Programme.Discover(Broadcast(network, 1) with
        {
            Related = [new RelatedProgramme(network, 1049, 9, RelationKind.Relayed)],
            Running = ProgrammeRunning.Running,
        }, At), Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Programme grouped = Assert.Single(
            await ProgrammesIn(reading).ListGroupedAsync(Cancel),
            programme => programme.NetworkId.Value == network);

        Assert.Equal(1, grouped.EventId.Value);
        Assert.Equal(ProgrammeRunning.Running, grouped.Running);
    }

    [Theory]
    [MemberData(nameof(WhatArrives))]
    public async Task BrEd002TheStoreAndTheCodeAbsorbTheSameVisitTheSameWay(string named)
    {
        int network = BroadcastIds.NextNetwork();
        Visit visit = Visits[named];
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository stored = ProgrammesIn(scope);
        var read = new HeldProgrammes();

        foreach (ProgrammeBroadcast held in visit.Held(network))
        {
            await stored.AddAsync(Programme.Discover(held, At), Cancel);
        }

        await read.AbsorbAsync(visit.Held(network), [], At, Cancel);
        Dictionary<ProgrammeId, long> storedBefore = await RevisionsAsync(network);
        Dictionary<ProgrammeId, long> readBefore = Revisions(read);

        ProgrammesAbsorbed byTheStore = await stored.AbsorbAsync(visit.Arriving(network), [], Later, Cancel);
        ProgrammesAbsorbed byTheCode = await read.AbsorbAsync(visit.Arriving(network), [], Later, Cancel);

        Assert.Equal(byTheCode, byTheStore);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        List<Programme> fromTheStore = await ListedAsync(reading, network);

        Assert.Equal(Spelt(read.Programmes), Spelt(fromTheStore));
        Assert.Equal(Moved(read.Programmes, readBefore), Moved(fromTheStore, storedBefore));
    }

    [Fact]
    public async Task TheGuideLeavesOutTheScheduleAReplacementHeardLaterCoversAndTheRowsStay()
    {
        int network = BroadcastIds.NextNetwork();
        await SupersededProgrammes.KeepAsync(
            container,
            ReplacedSchedule.On(network, SupersededProgrammes.Service, SupersededProgrammes.Start),
            []);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(reading);
        IReadOnlyList<Programme> guide = await programmes.ListForServicesAsync(
            [new ProgrammeService(network, SupersededProgrammes.Service)],
            SupersededProgrammes.Start.AddHours(-2),
            SupersededProgrammes.Start.AddHours(6),
            Cancel);

        Assert.Equal(ReplacedSchedule.Standing, SupersededProgrammes.Events(guide));
        Assert.NotNull(await programmes.FindAsync(
            SupersededProgrammes.Id(network, SupersededProgrammes.Service, ReplacedSchedule.Replaced[0]),
            Cancel));
    }

    [Fact]
    public async Task AProgrammeTheReplacementSupersededHasNoDetailYetIsStillReadForReservations()
    {
        int network = BroadcastIds.NextNetwork();
        await SupersededProgrammes.KeepAsync(
            container,
            ReplacedSchedule.On(network, SupersededProgrammes.Service, SupersededProgrammes.Start),
            []);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(reading);

        Assert.Null(await programmes.FindUnlessSupersededAsync(
            SupersededProgrammes.Id(network, SupersededProgrammes.Service, ReplacedSchedule.Replaced[4]),
            Cancel));
        Assert.Equal(
            "差し替えの番組",
            (await programmes.FindUnlessSupersededAsync(
                SupersededProgrammes.Id(network, SupersededProgrammes.Service, ReplacedSchedule.Replacement),
                Cancel))?.Name);
        Assert.NotNull(await programmes.FindAsync(
            SupersededProgrammes.Id(network, SupersededProgrammes.Service, ReplacedSchedule.Replaced[4]),
            Cancel));
    }

    [Fact]
    public async Task TheProgrammeOnAirIsTheReplacementRatherThanTheOneItReplaced()
    {
        int network = BroadcastIds.NextNetwork();
        DateTime now = SupersededProgrammes.Start.AddMinutes(130);
        await SupersededProgrammes.KeepAsync(
            container,
            ReplacedSchedule.On(network, SupersededProgrammes.Service, SupersededProgrammes.Start),
            []);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<Programme> onAir = await ProgrammesIn(reading).ListAsync(
            new ProgrammeWindow(network, SupersededProgrammes.Service, now, now.AddMicroseconds(1)),
            Cancel);

        Assert.Equal([ReplacedSchedule.Replacement], SupersededProgrammes.Events(onAir));
    }

    [Fact]
    public async Task TheStoreLeavesOutTheSameProgrammesAsTheCode()
    {
        int network = BroadcastIds.NextNetwork();
        Programme[] held = SupersededProgrammes.Varied(network);
        ArchivedProgramme[] kept = SupersededProgrammes.Kept(network);
        await SupersededProgrammes.KeepAsync(container, held, kept);
        var code = new HeldProgrammes();
        code.Programmes.AddRange(held);
        ProgrammeService[] both =
        [
            new ProgrammeService(network, SupersededProgrammes.Service),
            new ProgrammeService(network, SupersededProgrammes.Neighbour),
        ];

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IProgrammeRepository store = ProgrammesIn(reading);
        IReadOnlyList<Programme> guide = await store.ListForServicesAsync(
            both,
            SupersededProgrammes.Start.AddDays(-1),
            SupersededProgrammes.Start.AddDays(2),
            Cancel);

        Assert.Equal(
            [7, 9, 17, 19, 24, 25, 27, 28],
            SupersededProgrammes.Events(held.Where(programme => !programme.IsShadow)).Except(SupersededProgrammes.Events(guide)));
        Assert.Equal(
            SupersededProgrammes.Faces(await code.ListForServicesAsync(
                both,
                SupersededProgrammes.Start.AddDays(-1),
                SupersededProgrammes.Start.AddDays(2),
                Cancel)),
            SupersededProgrammes.Faces(guide));
        Assert.Equal(
            SupersededProgrammes.Faces(ProgrammeSupersession.Standing(held)),
            SupersededProgrammes.Faces(await FoundOneByOneAsync(store, held)));
        Assert.Equal(
            SupersededProgrammes.Faces(ProgrammeSupersession.Standing(held)),
            SupersededProgrammes.Faces(await FoundOneByOneAsync(code, held)));

        foreach (int minutes in new[] { -1440, 0, 15, 59, 60, 90, 130, 200, 245, 260 })
        {
            DateTime at = SupersededProgrammes.Start.AddMinutes(minutes);
            var moment = new ProgrammeWindow(network, SupersededProgrammes.Service, at, at.AddMicroseconds(1));

            Assert.Equal(
                SupersededProgrammes.Faces(Touching(await code.ListAsync(moment, Cancel), at)),
                SupersededProgrammes.Faces(await store.ListAsync(moment, Cancel)));
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    private async Task<IReadOnlyList<Programme>> ReadAfterAsync(long cursor)
    {
        await using AsyncServiceScope reading = container.CreateAsyncScope();

        return await ProgrammesIn(reading).ListAfterAsync(cursor, int.MaxValue, Cancel);
    }

    private async Task UntilWaitingOrDoneAsync(Task write)
    {
        await using AsyncServiceScope watching = container.CreateAsyncScope();

        for (int look = 0; look < 200 && !write.IsCompleted; look++)
        {
            bool waiting = await ContextIn(watching).Database
                .SqlQueryRaw<bool>("SELECT EXISTS (SELECT 1 FROM pg_locks WHERE NOT granted) AS \"Value\"")
                .SingleAsync(Cancel);

            if (waiting)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), Cancel);
        }
    }

    private static int NextNetwork() => BroadcastIds.NextNetwork();

    private static ProgrammeId Id(int network, int carried = 1)
        => new(new NetworkId(network), new ServiceId(1049), new EventId(carried));

    private static async Task<Programme[]> Ended(IProgrammeRepository programmes, int network)
        => [.. (await programmes.ListEndedBeforeAsync(At.AddHours(10), 5_000, Cancel))
            .Select(ended => ended.Programme)
            .Where(programme => programme.NetworkId.Value == network)];

    private static ProgrammeBroadcast Broadcast(int network, int carried = 1, DateTime? startsAt = null)
        => new(
            Id(network, carried),
            new TransportStreamId(32739),
            startsAt ?? At.AddHours(22),
            (startsAt ?? At.AddHours(22)).AddHours(1),
            "トップニュース先出し\U0001F211",
            "きょうのみどころ",
            IsShadow: false);

    private static ProgrammeBroadcast Aired(
        int network,
        int service,
        int carried,
        DateTime startsAt,
        int minutes,
        string name,
        int stream = 9002)
        => new(
            new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(stream),
            startsAt,
            startsAt.AddMinutes(minutes),
            name,
            string.Empty,
            IsShadow: false);

    private static ProgrammeBroadcast Carried(int network, int service, int carried, DateTime startsAt)
        => new(
            new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(32739),
            startsAt,
            startsAt.AddHours(1),
            "トップニュース先出し\U0001F211",
            "きょうのみどころ",
            IsShadow: false);

    private static IProgrammeRepository ProgrammesIn(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IProgrammeRepository>();

    private static CarinaDbContext ContextIn(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();

    private static IAtomicWrite AtomicIn(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IAtomicWrite>();

    private static List<string> Spelt(IEnumerable<Programme> programmes)
        =>
        [
            .. programmes
                .OrderBy(programme => programme.EventId.Value)
                .Select(programme => string.Join(
                    "|",
                    programme.EventId.Value,
                    programme.TransportStreamId.Value,
                    programme.StartsAt.ToString("O"),
                    programme.EndsAt?.ToString("O") ?? "open",
                    programme.Name,
                    programme.Summary,
                    programme.IsShadow,
                    string.Join(",", programme.Genres),
                    string.Join(",", programme.Items),
                    string.Join(",", programme.Related),
                    programme.HasSubtitles,
                    programme.Audio,
                    programme.Sounds,
                    programme.Video,
                    programme.Aspect,
                    programme.Running,
                    programme.Source,
                    programme.UpdatedAt.ToString("O"))),
        ];

    private static List<(int Event, string Revision)> Moved(
        IEnumerable<Programme> programmes,
        Dictionary<ProgrammeId, long> before)
        =>
        [
            .. programmes
                .OrderBy(programme => programme.EventId.Value)
                .Select(programme => (
                    programme.EventId.Value,
                    before.TryGetValue(programme.Id, out long was)
                        ? programme.Revision == was ? "kept" : "moved"
                        : "new")),
        ];

    private static Dictionary<ProgrammeId, long> Revisions(HeldProgrammes held)
        => held.Programmes.ToDictionary(programme => programme.Id, programme => programme.Revision);

    private async Task<Dictionary<ProgrammeId, long>> RevisionsAsync(int network)
    {
        await using AsyncServiceScope reading = container.CreateAsyncScope();

        return (await ListedAsync(reading, network)).ToDictionary(programme => programme.Id, programme => programme.Revision);
    }

    private static async Task<List<Programme>> ListedAsync(AsyncServiceScope scope, int network)
        =>
        [
            .. await ProgrammesIn(scope).ListAsync(
                new ProgrammeWindow(network, 1049, At.AddDays(-2), At.AddDays(2)),
                Cancel),
        ];

    private static async Task<IReadOnlyList<Programme>> FoundOneByOneAsync(
        IProgrammeRepository programmes,
        IReadOnlyList<Programme> held)
    {
        List<Programme> found = [];

        foreach (Programme programme in held)
        {
            if (await programmes.FindUnlessSupersededAsync(programme.Id, Cancel) is { } standing)
            {
                found.Add(standing);
            }
        }

        return found;
    }

    private static IEnumerable<Programme> Touching(IReadOnlyList<Programme> programmes, DateTime at)
        => programmes.Where(programme => programme.StartsAt <= at && (programme.EndsAt is not { } ends || ends > at));

    private sealed record Visit(Func<int, ProgrammeBroadcast[]> Held, Func<int, ProgrammeBroadcast[]> Arriving);
}
