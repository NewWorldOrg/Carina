using Carina.Domain.Channels;
using Carina.Domain.Library;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Library;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingLibraryRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime Airs = new(2026, 8, 26, 20, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task TheListHoldsWhatHasEndedAndLeavesWhatIsStillBeingWrittenToTheRecordingScreen()
    {
        await Clear();
        Recording ended = await Kept(Ended(Airs, "A programme"));
        Recording running = await Kept(Started(Airs.AddHours(1), "Still going"));

        LibraryRecordingPage page = await Search(Criteria());

        Assert.Equal([ended.Id], page.Rows.Select(row => row.Id));
        Assert.DoesNotContain(running.Id, page.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task TheNewestComesFirstAndFourThatStartedInTheSameSecondStillComeBackInASettledOrder()
    {
        await Clear();

        foreach (int carried in Enumerable.Range(0, 4))
        {
            await Kept(Ended(Airs, $"Together {carried}"));
        }

        IReadOnlyList<RecordingId> once = (await Search(Criteria())).Rows.Select(row => row.Id).ToArray();
        IReadOnlyList<RecordingId> again = (await Search(Criteria())).Rows.Select(row => row.Id).ToArray();

        Assert.Equal(4, once.Count);
        Assert.Equal(once, again);
    }

    [Fact]
    public async Task APageStopsAtTheSizeAskedForAndTheNextOneCarriesOnWithoutSkippingOrRepeatingARow()
    {
        await Clear();

        foreach (int carried in Enumerable.Range(0, 4))
        {
            await Kept(Ended(Airs, $"Together {carried}"));
        }

        IReadOnlyList<RecordingId> whole = (await Search(Criteria())).Rows.Select(row => row.Id).ToArray();
        LibraryRecordingPage first = await Search(Criteria(perPage: 2));
        Assert.NotNull(first.Next);
        LibraryRecordingPage second = await Search(Criteria(perPage: 2, after: first.Next));

        Assert.Equal(whole, [.. first.Rows.Concat(second.Rows).Select(row => row.Id)]);
        Assert.Null(second.Next);
    }

    [Fact]
    public async Task ARecordingThatStartedEarlierComesAfterOneThatStartedLater()
    {
        await Clear();
        Recording older = await Kept(Ended(Airs, "The older one"));
        Recording newer = await Kept(Ended(Airs.AddHours(1), "The newer one"));

        Assert.Equal([newer.Id, older.Id], (await Search(Criteria())).Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task ATitleCopiedOutOfTheGuideInFullWidthFindsTheRecordingOfIt()
    {
        await Clear();
        Recording kept = await Kept(Ended(Airs, "NEEDY GIRL"));

        Assert.Equal([kept.Id], (await Search(Criteria("ＮＥＥＤＹ"))).Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task ATitleWrittenInHalfWidthKanaIsFoundByTheWideFormAndTheOtherWayRound()
    {
        await Clear();
        Recording kept = await Kept(Ended(Airs, "ｷﾞｮｳｻﾞ特集"));

        Assert.Equal([kept.Id], (await Search(Criteria("ギョウザ"))).Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task AnUnderscoreInATitleIsAskedForLiterallyRatherThanAsAnyLetterAtAll()
    {
        await Clear();
        Recording kept = await Kept(Ended(Airs, "a_b"));
        await Kept(Ended(Airs.AddHours(1), "axb"));

        Assert.Equal([kept.Id], (await Search(Criteria("a_b"))).Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task APerCentSignInATitleIsAskedForLiterallyToo()
    {
        await Clear();
        Recording kept = await Kept(Ended(Airs, "100% orange"));
        await Kept(Ended(Airs.AddHours(1), "100 orange"));

        Assert.Equal([kept.Id], (await Search(Criteria("100%"))).Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task EveryWordHasToBeSomewhereInTheRecordedTextAndAnyOfTheThreeMayCarryIt()
    {
        await Clear();
        Recording kept = await Kept(Ended(Airs, "気象情報", "台風の見通し", "◇出演者 本名陽子"));
        await Kept(Ended(Airs.AddHours(1), "気象情報", "晴れの見通し", string.Empty));

        Assert.Equal([kept.Id], (await Search(Criteria("気象 本名陽子"))).Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task APerformerNamedOnlyInTheDetailIsFoundBecauseTheDetailIsSearchedToo()
    {
        await Clear();
        Recording kept = await Kept(Ended(Airs, "A programme", string.Empty, "◇出演者 …本名陽子…"));

        Assert.Equal([kept.Id], (await Search(Criteria("本名陽子"))).Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task AKeywordThatMatchesNothingLeavesAnEmptyPageRatherThanTheWholeLibrary()
    {
        await Clear();
        await Kept(Ended(Airs, "A programme"));

        Assert.Empty((await Search(Criteria("nothing like it"))).Rows);
    }

    [Fact]
    public async Task OnlyTheChannelsAskedForComeBack()
    {
        await Clear();
        Recording here = await Kept(Ended(Airs, "Here", channel: new ProgrammeService(32736, 1024)));
        await Kept(Ended(Airs.AddHours(1), "There", channel: new ProgrammeService(32737, 1032)));

        LibraryRecordingPage page = await Search(Criteria(conditions: new RecordingSearchConditions
        {
            Channels = [new ProgrammeService(32736, 1024)],
        }));

        Assert.Equal([here.Id], page.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task OnlyTheGenreAskedForComesBack()
    {
        await Clear();
        Recording sport = await Kept(Ended(Airs, "Sport", genre: 1));
        await Kept(Ended(Airs.AddHours(1), "News", genre: 8));

        LibraryRecordingPage page = await Search(Criteria(conditions: new RecordingSearchConditions { Genre = 1 }));

        Assert.Equal([sport.Id], page.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task OnlyWhatStartedInsideTheSpanComesBack()
    {
        await Clear();
        Recording inside = await Kept(Ended(Airs, "Inside"));
        await Kept(Ended(Airs.AddDays(-2), "Before"));
        await Kept(Ended(Airs.AddDays(2), "After"));

        LibraryRecordingPage page = await Search(
            RecordingSearchCriteria.For(null, Airs.AddDays(-1), Airs.AddDays(1)));

        Assert.Equal([inside.Id], page.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task NoSpanAtAllBringsBackEveryYearRatherThanTheLatestFewDays()
    {
        await Clear();
        await Kept(Ended(Airs.AddYears(-4), "Long ago"));
        await Kept(Ended(Airs, "Lately"));

        Assert.Equal(2, (await Search(Criteria())).Rows.Count);
    }

    [Fact]
    public async Task OnlyTheOutcomesAskedForComeBack()
    {
        await Clear();
        Recording truncated = await Kept(Truncated(Airs, "Cut short"));
        await Kept(Ended(Airs.AddHours(1), "Whole"));

        LibraryRecordingPage page = await Search(Criteria(conditions: new RecordingSearchConditions
        {
            Outcomes = [RecordingOutcome.Truncated],
        }));

        Assert.Equal([truncated.Id], page.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task NothingCountedTheseRecordingsSoTheyAreUnmeasuredRatherThanGood()
    {
        await Clear();
        Recording kept = await Kept(Ended(Airs, "Nobody counted"));

        LibraryRecordingPage page = await Search(Criteria(conditions: new RecordingSearchConditions
        {
            Quality = QualityLevel.Unmeasured,
        }));

        Assert.Equal([kept.Id], page.Rows.Select(row => row.Id));
        Assert.Empty((await Search(Criteria(conditions: new RecordingSearchConditions
        {
            Quality = QualityLevel.Good,
        }))).Rows);
    }

    [Fact]
    public async Task TheStandingARecordingIsFilteredOnIsTheOneTheQualityDomainGivesIt()
    {
        await Clear();
        Recording clean = await Kept(Measured(Airs, "Clean", DropCounters.Counted(0, 1_000_000)));
        Recording broken = await Kept(Measured(Airs.AddHours(1), "Broken", DropCounters.Counted(500_000, 1_000_000)));

        Assert.Equal(QualityLevel.Good, RecordingQuality.Of(clean.Counters, clean.ScrambledPackets));
        Assert.Equal(
            [clean.Id],
            (await Search(Criteria(conditions: new RecordingSearchConditions { Quality = QualityLevel.Good })))
                .Rows.Select(row => row.Id));
        Assert.Equal(
            [broken.Id],
            (await Search(Criteria(conditions: new RecordingSearchConditions
            {
                Quality = RecordingQuality.Of(broken.Counters, broken.ScrambledPackets),
            }))).Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task APageNarrowedByQualityStillStopsAtTheSizeAskedForAndSaysWhereToCarryOn()
    {
        await Clear();

        foreach (int carried in Enumerable.Range(0, 4))
        {
            await Kept(Ended(Airs.AddHours(carried), $"Nobody counted {carried}"));
        }

        LibraryRecordingPage page = await Search(Criteria(
            perPage: 2,
            conditions: new RecordingSearchConditions { Quality = QualityLevel.Unmeasured }));

        Assert.Equal(2, page.Rows.Count);
        Assert.NotNull(page.Next);

        LibraryRecordingPage rest = await Search(Criteria(
            perPage: 2,
            after: page.Next,
            conditions: new RecordingSearchConditions { Quality = QualityLevel.Unmeasured }));

        Assert.Equal(2, rest.Rows.Count);
        Assert.Null(rest.Next);
    }

    [Fact]
    public async Task ARowCarriesTheSizeSomebodyReadOffTheDiskAndTheMomentTheyReadIt()
    {
        await Clear();
        await Kept(Ended(Airs, "A programme"));

        LibraryRecordingSummary row = Assert.Single((await Search(Criteria())).Rows);

        Assert.Equal(20_000_000, row.FileSizeObserved);
        Assert.NotNull(row.ObservedAt);
    }

    [Fact]
    public async Task WhatIsAskedForByIdComesBackEvenWhileItIsStillBeingWritten()
    {
        await Clear();
        Recording running = await Kept(Started(Airs, "Still going"));

        await using CarinaDbContext reading = database.Open();
        Recording? read = await new RecordingLibraryRepository(reading).FindAsync(running.Id, Cancel);

        Assert.NotNull(read);
        Assert.True(read.IsInFlight);
    }

    [Fact]
    public async Task AnIdNothingWasRecordedUnderComesBackAsNothing()
    {
        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new RecordingLibraryRepository(reading).FindAsync(RecordingId.New(), Cancel));
    }

    [Fact]
    public async Task DeletingARecordingThatHasEndedTakesTheOneRowWithIt()
    {
        await Clear();
        Recording kept = await Kept(Ended(Airs, "A programme"));

        await using CarinaDbContext writing = database.Open();

        Assert.Equal(1, await new RecordingLibraryRepository(writing).DeleteAsync(kept.Id, Cancel));
        Assert.Empty((await Search(Criteria())).Rows);
    }

    [Fact]
    public async Task DeletingARecordingThatIsStillBeingWrittenChangesNoRowAtAll()
    {
        await Clear();
        Recording running = await Kept(Started(Airs, "Still going"));

        await using CarinaDbContext writing = database.Open();

        Assert.Equal(0, await new RecordingLibraryRepository(writing).DeleteAsync(running.Id, Cancel));

        await using CarinaDbContext reading = database.Open();
        Assert.NotNull(await new RecordingLibraryRepository(reading).FindAsync(running.Id, Cancel));
    }

    [Fact]
    public async Task DeletingSomethingThatIsNotThereChangesNoRowAtAll()
    {
        await using CarinaDbContext writing = database.Open();

        Assert.Equal(0, await new RecordingLibraryRepository(writing).DeleteAsync(RecordingId.New(), Cancel));
    }

    private static RecordingSearchCriteria Criteria(
        string? keyword = null,
        int? perPage = null,
        RecordingCursor? after = null,
        RecordingSearchConditions? conditions = null)
        => RecordingSearchCriteria.For(
            keyword,
            null,
            null,
            after: after,
            perPage: perPage,
            conditions: conditions)!;

    private async Task<LibraryRecordingPage> Search(RecordingSearchCriteria? criteria)
    {
        await using CarinaDbContext reading = database.Open();

        return await new RecordingLibraryRepository(reading).SearchAsync(criteria!, Cancel);
    }

    private async Task<Recording> Kept(Recording recording)
    {
        await using CarinaDbContext writing = database.Open();
        writing.Add(recording);
        await writing.SaveChangesAsync(Cancel);

        return recording;
    }

    private async Task Clear()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Database.ExecuteSqlRawAsync("DELETE FROM recording", Cancel);
    }

    private static Recording Started(
        DateTime startedAt,
        string name,
        string summary = "",
        string extended = "",
        ProgrammeService? channel = null,
        int? genre = null)
    {
        RecordingId id = RecordingId.New();
        ProgrammeService on = channel ?? new ProgrammeService(32736, 1024);

        return Recording.Begin(
            id,
            null,
            new ProgrammeRef(
                new NetworkId(on.NetworkId),
                new ServiceId(on.ServiceId),
                new EventId(4001),
                startedAt),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            startedAt,
            startedAt.AddMinutes(30),
            new ProgrammeSnapshot(
                name,
                summary,
                extended,
                genre is { } kind ? [new ProgrammeGenre(kind, 0)] : [],
                startedAt),
            null,
            BroadcastGroupRole.Standalone,
            startedAt,
            new TunerDeviceId("pt3-0"));
    }

    private static Recording Ended(
        DateTime startedAt,
        string name,
        string summary = "",
        string extended = "",
        ProgrammeService? channel = null,
        int? genre = null)
    {
        Recording recording = Started(startedAt, name, summary, extended, channel, genre);

        recording.Abort(startedAt.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 20_000_000, startedAt.AddMinutes(30));

        return recording;
    }

    private static Recording Truncated(DateTime startedAt, string name)
    {
        Recording recording = Started(startedAt, name);

        recording.Note(new OutcomeDetail(RecordingFault.DriverLost, null, "gone", startedAt.AddMinutes(10)));
        recording.Settle(RecordingOutcome.Truncated, 5_000_000, startedAt.AddMinutes(10));

        return recording;
    }

    private static Recording Measured(DateTime startedAt, string name, DropCounters counters)
    {
        Recording recording = Started(startedAt, name);

        recording.Measure(counters, DropTimeline.Unlocated, 0, 0, startedAt.AddMinutes(10));
        recording.Abort(startedAt.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 20_000_000, startedAt.AddMinutes(30));

        return recording;
    }
}
