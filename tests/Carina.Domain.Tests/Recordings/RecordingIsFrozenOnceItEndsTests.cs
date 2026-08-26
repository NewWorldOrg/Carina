using System.Collections;
using System.Globalization;
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
            .. Declared(BindingFlags.Public | BindingFlags.Instance)
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            ["Abort", "Acquire", "Extend", "Illustrate", "Interrupt", "Measure", "Note", "Resume", "Settle", "Wrote"],
            offered);
        Assert.Equal(offered.Length, Declared(BindingFlags.Public | BindingFlags.Instance).Length);
    }

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
    public void TheOnlyThingsARecordingOffersWithoutOneAreTheTwoThatMakeOne()
        => Assert.Equal(
            ["Begin", "Rehydrate"],
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
            DateTime moment => moment.ToString("O", CultureInfo.InvariantCulture),
            IEnumerable listed => string.Join('|', listed.Cast<object?>().Select(Rendered)),
            IFormattable measured => measured.ToString(null, CultureInfo.InvariantCulture),
            _ => held.ToString() ?? "<none>",
        };
}
