using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Encodings;

public sealed class ArtefactRewriteDomainServiceTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 12, 3, 0, 0, TimeSpan.Zero);

    private readonly HandTurnedClock clock = new(At);

    private readonly HeldOpenings openings = new();

    private readonly EncodeJob job = Standing();

    private ArtefactRewriteDomainService Service => new(openings, clock);

    [Fact(DisplayName = "BR-ED2-019: an artefact nobody opened since this process came up is written again")]
    public void AnArtefactNobodyOpenedIsWrittenAgain() => Assert.False(Service.IsLeftAlone(job));

    [Fact(DisplayName = "BR-ED2-019: an artefact opened in the last 30 minutes is left alone, and is written again once they have passed")]
    public void AnArtefactOpenedLatelyIsLeftAlone()
    {
        openings.Opened(job.OutputRoot, job.ArtefactName!.Value);
        clock.Turn(ArtefactRewriteDomainService.LeftAloneAfterAnOpening - TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromMinutes(30), ArtefactRewriteDomainService.LeftAloneAfterAnOpening);
        Assert.True(Service.IsLeftAlone(job));

        clock.Turn(TimeSpan.FromSeconds(1));

        Assert.False(Service.IsLeftAlone(job));
    }

    [Fact(DisplayName = "BR-ED2-019: an opening of another file under the same output root leaves the artefact to be written again")]
    public void AnOpeningOfAnotherFileDoesNotCount()
    {
        openings.Opened(job.OutputRoot, "another.mp4");

        Assert.False(Service.IsLeftAlone(job));
    }

    private static EncodeJob Standing()
    {
        RecordingId recording = RecordingId.New();
        EncodeProfileId profile = EncodeProfileId.New();
        EncodeJob made = EncodeJob.Queue(EncodeJobId.New(), recording, profile, EncodeDestinationId.New(), new OutputRoot("shelf"), At.UtcDateTime.AddHours(-2));
        made.Start(At.UtcDateTime.AddHours(-2));
        made.Name(EncodeFileName.Artefact(recording, profile));
        made.Complete(At.UtcDateTime.AddHours(-1));

        return made;
    }

    private sealed class HeldOpenings : IArtefactOpenings
    {
        private readonly Dictionary<(string Root, string Name), DateTimeOffset> opened = [];

        public void Opened(OutputRoot root, string name) => opened[(root.Value, name)] = At;

        public DateTimeOffset? LastOpened(OutputRoot root, string name)
            => opened.TryGetValue((root.Value, name), out DateTimeOffset at) ? at : null;
    }
}
