using System.Collections;
using System.Reflection;

using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingIsFrozenOnceItEndsTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    private static readonly DateTime Later = RecordingFactory.Now.AddHours(1);

    [Fact]
    public void ARecordingOffersTheseAndNothingElse()
    {
        string[] offered =
        [
            .. typeof(Recording)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            ["Abort", "Acquire", "Extend", "Illustrate", "Interrupt", "Measure", "Note", "Resume", "Settle", "Wrote"],
            offered);
    }

    [Fact]
    public void EveryOneOfThemButTheOneThatDrawsThePictureRefusesOnceTheRecordingHasEnded()
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
        Assert.Throws<InvalidOperationException>(() => recording.Note(RecordingFactory.Fault()));
        Assert.Throws<InvalidOperationException>(() => recording.Resume(Later));
        Assert.Throws<InvalidOperationException>(
            () => recording.Settle(RecordingOutcome.Complete, 1, Later));
        Assert.Throws<InvalidOperationException>(() => recording.Wrote(TimeSpan.FromMinutes(1)));

        recording.Illustrate(ThumbnailState.Ready);

        Assert.Equal(ThumbnailState.Ready, recording.ThumbnailState);
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
        Assert.Equal(41, before.Count);
    }

    private static Recording Settled()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Settle(RecordingOutcome.Truncated, 1_200_000, Later);

        return recording;
    }

    private static IReadOnlyDictionary<string, string> Read(Recording recording)
        => typeof(Recording)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != nameof(Recording.ThumbnailShowsAnUnfinishedRecording))
            .ToDictionary(
                property => property.Name,
                property => Rendered(property.GetValue(recording)),
                StringComparer.Ordinal);

    private static string Rendered(object? held)
        => held switch
        {
            null => "<none>",
            string text => text,
            IEnumerable listed => string.Join('|', listed.Cast<object?>().Select(Rendered)),
            _ => held.ToString() ?? "<none>",
        };
}
