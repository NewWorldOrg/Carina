using System.Collections;
using System.Globalization;
using System.Reflection;

using Carina.Domain.DataBroadcast;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingIsFrozenOnceItEndsTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    private static readonly DateTime Later = RecordingFactory.Now.AddHours(1);

    private static readonly QualityBands Bands =
        QualityThresholdStanding.Bands(QualityThresholdStanding.Over([], RecordingFactory.Now));

    private static readonly HashSet<string> Derived = new(StringComparer.Ordinal)
    {
        nameof(Recording.ThumbnailShowsAnUnfinishedRecording),
        nameof(Recording.LeftScrambled),
        nameof(Recording.Encodability),
        nameof(Recording.FollowsItsProgramme),
        nameof(Recording.CountedUntil),
        nameof(Recording.IsInterrupted),
        nameof(Recording.AwaitsAThumbnail),
        nameof(Recording.AwaitsCaptions),
    };

    [Fact]
    public void ARecordingOffersTheseChangesAndNothingElse()
    {
        string[] offered = Named(Declared(BindingFlags.Public | BindingFlags.Instance).Where(Changes));

        Assert.Equal(
            [
                "Abort", "Acquire", "Adopt", "Caption", "Conclude", "DataBroadcastAgain", "DataBroadcastFailed",
                "DataBroadcastTaken", "Descrambled", "Erased", "Extend", "FailOnAFullDisk", "Illustrate", "Interrupt", "Measure",
                "Missed", "Note", "NoteEndUndecided", "Prechecked", "ReachedItsEnd", "Resume", "Settle", "SettleOnWhatWasLeft",
                "Wrote",
            ],
            offered);
        Assert.Equal(offered.Length, Declared(BindingFlags.Public | BindingFlags.Instance).Count(Changes));
    }

    [Fact]
    public void ARecordingAnswersTheseQuestionsAndNothingElse()
        => Assert.Equal(
            ["Follows", "IsDueToStop", "IsOver", "KeepsCountingAt", "StoppedWritingAt"],
            Named(Declared(BindingFlags.Public | BindingFlags.Instance).Where(method => !Changes(method))));

    [Fact]
    public void AskingARecordingAQuestionMovesNothing()
    {
        foreach (Recording recording in new[] { RecordingFactory.Started(), Settled() })
        {
            IReadOnlyDictionary<string, string> before = Read(recording);

            _ = recording.Follows(new RecordingGap(Later, Later.AddSeconds(3)));
            _ = recording.IsDueToStop(Later);
            _ = recording.IsOver(Later);
            _ = recording.KeepsCountingAt(Later);
            _ = recording.StoppedWritingAt(Now, Later);

            Assert.Equal(before, Read(recording));
        }
    }

    private static bool Changes(MethodInfo method) => method.ReturnType == typeof(void);

    private static string[] Named(IEnumerable<MethodInfo> methods)
        =>
        [
            .. methods
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

    [Fact]
    public void NothingChangesARecordingFromOutsideItsOwnMethods()
    {
        Assert.Empty(typeof(Recording)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name));
        Assert.Empty(typeof(Recording)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(field => field.Name));
    }

    [Fact]
    public void TheOnlyThingsARecordingOffersWithoutOneAreTheThreeThatMakeOne()
        => Assert.Equal(
            ["Begin", "CarriedOver", "Rehydrate"],
            Declared(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());

    private static MethodInfo[] Declared(BindingFlags reaching)
        => [.. typeof(Recording)
            .GetMethods(reaching | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)];

    [Fact]
    public void EveryOneOfThemButThePictureTheCaptionsTheDataBroadcastTheErasureAndTheDescramblingRefusesOnceTheRecordingHasEnded()
    {
        Recording recording = Settled();

        Assert.Throws<InvalidOperationException>(() => recording.Abort(Later));
        Assert.Throws<InvalidOperationException>(() => recording.Acquire(RecordingFactory.Tuner));
        Assert.Throws<InvalidOperationException>(() => recording.Extend(Later.AddHours(1)));
        Assert.Throws<InvalidOperationException>(
            () => recording.Interrupt(RecordingFault.DriverLost, Later));
        Assert.Throws<InvalidOperationException>(() => recording.Measure(
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            Later));
        Assert.Throws<InvalidOperationException>(
            () => recording.Missed(new RecordingGap(Later, Later.AddSeconds(3))));
        Assert.Throws<InvalidOperationException>(() => recording.Note(RecordingFactory.Fault()));
        Assert.Throws<InvalidOperationException>(() => recording.Resume(Later));
        Assert.Throws<InvalidOperationException>(
            () => recording.Settle(RecordingOutcome.Complete, 1, Later));
        Assert.Throws<InvalidOperationException>(() => recording.Wrote(TimeSpan.FromMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => recording.FailOnAFullDisk(1, Later));
        Assert.Throws<InvalidOperationException>(() => recording.SettleOnWhatWasLeft(false, 1, Bands, Later));
        Assert.Throws<InvalidOperationException>(
            () => recording.Conclude(1, ExpectedBitrate.Terrestrial, Bands, Later));
        Assert.Throws<InvalidOperationException>(() => recording.ReachedItsEnd(null, Later));
        Assert.Throws<InvalidOperationException>(() => recording.Adopt("pt3-4"));
        Assert.Throws<InvalidOperationException>(() => recording.NoteEndUndecided(Later));
        Assert.Throws<InvalidOperationException>(() => recording.Prechecked(
            DiskPrecheckVerdict.Of(DiskShortfall.NoRoomLeft, 1, 0, 1),
            Later));

        recording.Illustrate(ThumbnailState.Ready);
        recording.Caption(CaptionState.Ready, 4, Later);
        recording.DataBroadcastTaken(3, Later);
        recording.Erased(RecordingErasure.Refused(ErasureFault.FileLeftBehind, "permission denied", 1), Later);
        recording.Descrambled(Later);

        Assert.Equal(ThumbnailState.Ready, recording.ThumbnailState);
        Assert.Equal(CaptionState.Ready, recording.CaptionState);
        Assert.Equal(DataBroadcastState.Coming, recording.DataBroadcastState);
        Assert.Equal(Later, recording.LeftBehindAt);
        Assert.Equal(Later, recording.DescrambledAt);
    }

    [Fact]
    public void DescramblingMovesTheColumnThatSaysSoAndNoOthers()
    {
        Recording recording = Settled();
        IReadOnlyDictionary<string, string> before = Read(recording);

        recording.Descrambled(Later);

        IReadOnlyDictionary<string, string> after = Read(recording);
        string[] moved =
        [
            .. before.Where(held => !string.Equals(after[held.Key], held.Value, StringComparison.Ordinal))
                .Select(held => held.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal([nameof(Recording.DescrambledAt)], moved);
    }

    [Fact]
    public void KeepingWhatAnErasureLeftBehindMovesTheColumnsThatSaySoAndNoOthers()
    {
        Recording recording = Settled();
        IReadOnlyDictionary<string, string> before = Read(recording);

        recording.Erased(RecordingErasure.Refused(ErasureFault.FileLeftBehind, "permission denied", 2), Later);

        IReadOnlyDictionary<string, string> after = Read(recording);
        string[] moved =
        [
            .. before.Where(held => !string.Equals(after[held.Key], held.Value, StringComparison.Ordinal))
                .Select(held => held.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal([nameof(Recording.FilesLeftBehind), nameof(Recording.LeftBehindAt)], moved);
    }

    [Theory]
    [InlineData(ThumbnailState.Ready, null)]
    [InlineData(ThumbnailState.Skipped, null)]
    [InlineData(ThumbnailState.Failed, ThumbnailFault.TimedOut)]
    public void DrawingThePictureMovesTheColumnsThatAreThePictureAndNoOthers(
        ThumbnailState state,
        ThumbnailFault? fault)
    {
        Recording recording = Settled();
        IReadOnlyDictionary<string, string> before = Read(recording);

        recording.Illustrate(state, fault);

        IReadOnlyDictionary<string, string> after = Read(recording);
        string[] moved =
        [
            .. before.Where(held => !string.Equals(after[held.Key], held.Value, StringComparison.Ordinal))
                .Select(held => held.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.NotEmpty(moved);
        Assert.Empty(moved.Except(
            [nameof(Recording.ThumbnailState), nameof(Recording.ThumbnailFault)],
            StringComparer.Ordinal));
        Assert.Equal(66, before.Count);
    }

    [Theory]
    [InlineData(CaptionState.Ready, 7)]
    [InlineData(CaptionState.Absent, null)]
    [InlineData(CaptionState.Failed, null)]
    public void TakingTheCaptionsMovesTheColumnsThatAreTheCaptionsAndNoOthers(CaptionState state, int? pictures)
    {
        Recording recording = Settled();
        IReadOnlyDictionary<string, string> before = Read(recording);

        recording.Caption(state, pictures, Later);

        IReadOnlyDictionary<string, string> after = Read(recording);
        string[] moved =
        [
            .. before.Where(held => !string.Equals(after[held.Key], held.Value, StringComparison.Ordinal))
                .Select(held => held.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.NotEmpty(moved);
        Assert.Empty(moved.Except(
            [
                nameof(Recording.CaptionState),
                nameof(Recording.CaptionsMadeAt),
                nameof(Recording.CaptionPictures),
                nameof(Recording.CaptionAttempts),
            ],
            StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(-1)]
    public void TakingTheDataBroadcastMovesTheColumnsThatAreTheDataBroadcastAndNoOthers(int modules)
    {
        Recording recording = Settled();
        IReadOnlyDictionary<string, string> before = Read(recording);

        if (modules < 0)
        {
            recording.DataBroadcastFailed(Later);
        }
        else
        {
            recording.DataBroadcastTaken(modules, Later);
        }

        recording.Descrambled(Later);

        IReadOnlyDictionary<string, string> after = Read(recording);
        string[] moved =
        [
            .. before.Where(held => !string.Equals(after[held.Key], held.Value, StringComparison.Ordinal))
                .Select(held => held.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.NotEmpty(moved);
        Assert.Empty(moved.Except(
            [
                nameof(Recording.DataBroadcast),
                nameof(Recording.DataBroadcastState),
                nameof(Recording.DataBroadcastMadeAt),
                nameof(Recording.DataBroadcastModules),
                nameof(Recording.DataBroadcastAttempts),
                nameof(Recording.DescrambledAt),
            ],
            StringComparer.Ordinal));
    }

    private static Recording Settled()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Note(RecordingFactory.Fault(RecordingFault.ScramblingUnresolved));
        recording.Settle(RecordingOutcome.Truncated, 1_200_000, Later);

        return recording;
    }

    private static IReadOnlyDictionary<string, string> Read(Recording recording)
        => typeof(Recording)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !Derived.Contains(property.Name))
            .ToDictionary(
                property => property.Name,
                property => Rendered(property.GetValue(recording)),
                StringComparer.Ordinal);

    private static string Rendered(object? held)
        => held switch
        {
            null => "<none>",
            string text => text,
            DateTime moment => moment.ToString("O", CultureInfo.InvariantCulture),
            IEnumerable listed => string.Join('|', listed.Cast<object?>().Select(Rendered)),
            IFormattable measured => measured.ToString(null, CultureInfo.InvariantCulture),
            _ => held.ToString() ?? "<none>",
        };
}
