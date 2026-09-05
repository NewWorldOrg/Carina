using System.Collections.Concurrent;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
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

    public static readonly TimeSpan Whole = TimeSpan.FromSeconds(10);

    public const string Broadcast = "the broadcast";

    public EncodeHarness(bool workingBeside = true)
    {
        Room = new TempTree();
        Workshop = workingBeside ? null : new TempTree();
        Settings = new EncodeSettings { WorkedIn = Workshop?.Root, StalledAfter = TimeSpan.FromSeconds(20) };
        Mounts = new IntegritySettings { OutputRoots = [new StorageRootPath(Primary, Room.Root)] };
        Clock = new HandTurnedClock(new DateTimeOffset(2026, 9, 5, 4, 0, 0, TimeSpan.Zero));
        MachineReader = Machine;
        LengthReader = Lengths;
    }

    public TempTree Room { get; }

    public TempTree? Workshop { get; }

    public EncodeSettings Settings { get; set; }

    public IntegritySettings Mounts { get; }

    public EncodePlaces Places => new(Mounts, Settings);

    public TimeProvider Clock { get; set; }

    public HeldEncodeJobs Jobs { get; } = new();

    public HeldEncodeScratch Scratch { get; } = new();

    public HeldEncodeProfiles Profiles { get; } = new();

    public HeldRecordingRows Recordings { get; } = new();

    public AskedMachine Machine { get; } = new();

    public MeasuredLengths Lengths { get; } = new();

    public IMachineCapabilityReader MachineReader { get; set; }

    public ISourceLengthReader LengthReader { get; set; }

    public MachineSettings Programmes { get; set; } = new();

    public IRenameProbe Probe { get; set; } = new DirectoryRenameProbe();

    public HeardOf<EncodeArtefactPlacer> PlacerLog { get; } = new();

    public HeardOf<EncodeScratchCleaner> CleanerLog { get; } = new();

    public HeardOf<EncodeJobRunner> RunnerLog { get; } = new();

    public string WorkDirectory => (Workshop ?? Room).Root;

    public EncodeArtefactPlacer Placer => new(Jobs, Scratch, Places, Probe, Clock, PlacerLog);

    public EncodeScratchCleaner Cleaner => new(Scratch, Places, Clock, CleanerLog);

    public EncodeScratchFiles ScratchFiles => new(Scratch, Places, Clock);

    public EncodeJobRunner Runner => new(
        Jobs,
        Profiles,
        Recordings,
        Places,
        ScratchFiles,
        Placer,
        Cleaner,
        MachineReader,
        LengthReader,
        Programmes,
        Settings,
        Clock,
        RunnerLog);

    public EncodeProfile Defined(EncodeCodec codec = EncodeCodec.H264)
    {
        EncodeProfile profile = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Viewing"),
            codec,
            EncodeResolution.AsSource,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            Queued);
        Profiles.Profiles.Add(profile);

        return profile;
    }

    public Recording Recorded(string content = Broadcast, OutputRoot? root = null)
    {
        Recording recording = Ended(root ?? Primary, 1064, content.Length);

        if (root is null)
        {
            File.WriteAllText(SourcePathOf(recording), content);
        }

        return recording;
    }

    public Recording RecordedFrom(string written, int serviceId)
    {
        Recording recording = Ended(Primary, serviceId, new FileInfo(written).Length);
        File.Copy(written, SourcePathOf(recording));

        return recording;
    }

    private Recording Ended(OutputRoot root, int serviceId, long size)
    {
        var id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32741), new ServiceId(serviceId), new Carina.Domain.Programmes.EventId(8981), Queued),
            root,
            RecordingFileName.For(id, ".ts"),
            Queued,
            Queued + Whole,
            new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], Queued),
            null,
            BroadcastGroupRole.Standalone,
            Queued,
            new TunerDeviceId("synthetic-0"));
        recording.Wrote(Whole);
        recording.Abort(Queued + Whole);
        recording.Settle(RecordingOutcome.Complete, size, Queued + Whole);
        Recordings.Rows.Add(recording);

        return recording;
    }

    public string SourcePathOf(Recording recording) => Path.Combine(Room.Root, recording.FileName.Value);

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
            EncodeFileName.Artefact(recording, profile),
            null,
            null,
            null);
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

    public string WorkPathOf(EncodeJob job) => Path.Combine(WorkDirectory, job.WorkFileName.Value);

    public string ArtefactPathOf(EncodeJob job)
        => Path.Combine(Room.Root, EncodeFileName.Artefact(job.RecordingId, job.ProfileId).Value);

    /// <summary>
    /// A programme standing in for ffmpeg: a shell script that is handed ffmpeg's arguments, the
    /// destination last, and writes what it is told to on standard output and standard error.
    /// </summary>
    public string Standing(string script)
    {
        string path = Room.Under($"programme-{Guid.NewGuid():N}.sh");
        File.WriteAllText(path, "#!/bin/sh\nfor argument in \"$@\"; do destination=$argument; done\n" + script + "\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Programmes = new MachineSettings { Programme = path };

        return path;
    }

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

internal sealed class HeldRecordingRows : IRecordingRepository
{
    public List<Recording> Rows { get; } = [];

    public Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
        => Task.FromResult(Rows.FirstOrDefault(row => row.Id.Equals(id)));

    public Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>([.. Rows.Where(row => row.IsInFlight)]);

    public Task<IReadOnlyList<Recording>> ListForReservationAsync(ReservationId reservationId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>([.. Rows.Where(row => reservationId.Equals(row.ReservationId))]);

    public Task AddAsync(Recording recording, CancellationToken cancellationToken)
    {
        Rows.Add(recording);

        return Task.CompletedTask;
    }

    public Task SaveAsync(Recording recording, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class AskedMachine : IMachineCapabilityReader
{
    public MachineCapabilities Can { get; set; } = MachineCapabilities.Of(
        CardStanding.NodeMissing,
        [Faculty.EncodeH264OnTheProcessor, Faculty.DecodeAribCaptions],
        "no render node was handed to this container");

    public int Times { get; private set; }

    public Task<MachineCapabilities> ReadAsync(CancellationToken cancellationToken)
    {
        Times++;

        return Task.FromResult(Can);
    }
}

internal sealed class MeasuredLengths : ISourceLengthReader
{
    public SourceLengthReading Reading { get; set; } = SourceLengthReading.Read(EncodeHarness.Whole);

    public List<string> Asked { get; } = [];

    public Task<SourceLengthReading> ReadAsync(string source, CancellationToken cancellationToken)
    {
        Asked.Add(source);

        return Task.FromResult(Reading);
    }
}
