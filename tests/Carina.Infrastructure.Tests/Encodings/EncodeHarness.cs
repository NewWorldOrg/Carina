using System.Collections.Concurrent;

using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Tests.Integrity;
using Carina.Infrastructure.Tests.Thumbnails;
using Carina.TestSupport;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Tests.Encodings;

internal sealed class EncodeHarness : IDisposable
{
    public static readonly OutputRoot Primary = new("primary");

    public static readonly DateTime Queued = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime Started = new(2026, 9, 5, 3, 0, 5, DateTimeKind.Utc);

    public EncodeHarness(bool workingBeside = true)
    {
        Room = new TempTree();
        Workshop = workingBeside ? null : new TempTree();
        Settings = new EncodeSettings { WorkedIn = Workshop?.Root };
        Mounts = new IntegritySettings { OutputRoots = [new StorageRootPath(Primary, Room.Root)] };
        Places = new EncodePlaces(Mounts, Settings);
        Clock = new HandTurnedClock(new DateTimeOffset(2026, 9, 5, 4, 0, 0, TimeSpan.Zero));
    }

    public TempTree Room { get; }

    public TempTree? Workshop { get; }

    public EncodeSettings Settings { get; }

    public IntegritySettings Mounts { get; }

    public EncodePlaces Places { get; }

    public HandTurnedClock Clock { get; }

    public HeldEncodeJobs Jobs { get; } = new();

    public HeldEncodeScratch Scratch { get; } = new();

    public IRenameProbe Probe { get; set; } = new DirectoryRenameProbe();

    public HeardOf<EncodeArtefactPlacer> PlacerLog { get; } = new();

    public HeardOf<EncodeScratchCleaner> CleanerLog { get; } = new();

    public string WorkDirectory => (Workshop ?? Room).Root;

    public EncodeArtefactPlacer Placer => new(Jobs, Scratch, Places, Probe, Clock, PlacerLog);

    public EncodeScratchCleaner Cleaner => new(Scratch, Places, Clock, CleanerLog);

    public EncodeScratchFiles ScratchFiles => new(Scratch, Places, Clock);

    public EncodeJob Running(RecordingId? recording = null, EncodeProfileId? profile = null)
    {
        EncodeJob job = EncodeJob.Queue(
            EncodeJobId.New(),
            recording ?? RecordingId.New(),
            profile ?? EncodeProfileId.New(),
            EncodeDestinationId.New(),
            Primary,
            Queued);
        job.Start(Started);
        Jobs.Jobs.Add(job);

        return job;
    }

    public EncodeJob RunningAgainWithItsName(RecordingId recording, EncodeProfileId profile)
    {
        EncodeJob job = EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording,
            profile,
            EncodeDestinationId.New(),
            Primary,
            EncodeJobStatus.Running,
            2,
            Queued,
            Started,
            null,
            null,
            EncodeFileName.Artefact(recording, profile));
        Jobs.Jobs.Add(job);

        return job;
    }

    public string WorkFileOf(EncodeJob job, string content)
    {
        string path = Path.Combine(WorkDirectory, job.WorkFileName.Value);
        File.WriteAllText(path, content);
        Scratch.Files.Add(EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            job.Id,
            EncodeScratchKind.WorkFile,
            Primary,
            job.WorkFileName,
            Queued));

        return path;
    }

    public string ArtefactPathOf(EncodeJob job)
        => Path.Combine(Room.Root, EncodeFileName.Artefact(job.RecordingId, job.ProfileId).Value);

    public void Dispose()
    {
        Room.Dispose();
        Workshop?.Dispose();
    }
}

internal sealed class ScriptedProbe(RenameVerdict verdict) : IRenameProbe
{
    public ConcurrentQueue<(string From, string To)> Asked { get; } = new();

    public RenameVerdict Probe(string from, string to)
    {
        Asked.Enqueue((from, to));

        return verdict;
    }
}
