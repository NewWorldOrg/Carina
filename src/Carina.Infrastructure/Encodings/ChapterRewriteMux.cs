using System.Globalization;

using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// What writing a standing artefact again with other chapters came to: the copy written, when it was, and why
/// not otherwise.
/// </summary>
public sealed record ChapterRewriteMade(string? Rewritten, string Note)
{
    public bool Made => Rewritten is not null;

    public static ChapterRewriteMade Written(string rewritten) => new(rewritten, string.Empty);

    public static ChapterRewriteMade Failed(string note) => new(null, note);
}

/// <summary>
/// The names the chapters and the copy are written under for one try, so that no two tries share a name.
/// </summary>
public sealed record ChapterRewriteNames(EncodeFileName Chapters, EncodeFileName Chaptered)
{
    public static ChapterRewriteNames Tried(EncodeJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        EncodeScratchFileId tried = EncodeScratchFileId.New();

        return new ChapterRewriteNames(
            EncodeFileName.Chapters(job.RecordingId, job.Id, job.Attempt, tried),
            EncodeFileName.Chaptered(job.RecordingId, job.Id, job.Attempt, tried));
    }
}

/// <summary>
/// Writes a copy of a standing artefact with its chapters in place of the ones it carries, beside the work of
/// the job that made it: the chapters and the copy are written into the ledger as scratch first, the picture,
/// sound and subtitle tracks are copied as they are, a subtitle track is turned off by default again, and the
/// copy is checked against the artefact and against the chapters before it is handed back. The artefact itself
/// is only read.
/// </summary>
public sealed class ChapterRewriteMux(EncodeScratchFiles scratch, MachineSettings machine, TimeProvider clock)
{
    public static readonly TimeSpan LongestCopy = CaptionTrackMux.LongestCopy;

    public async Task<ChapterRewriteMade> MakeAsync(
        EncodeJob job,
        string artefact,
        IReadOnlyList<ChapterSegment> wanted,
        ChapterRewriteNames names,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrEmpty(artefact);
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(names);

        string? chapters = wanted.Count is 0
            ? null
            : await scratch.RecordAsync(job, EncodeScratchKind.Chapters, names.Chapters, cancellationToken);
        string? rewritten = wanted.Count is not 0 && chapters is null
            ? null
            : await scratch.RecordAsync(job, EncodeScratchKind.ChapteredWork, names.Chaptered, cancellationToken);

        if (rewritten is null)
        {
            return ChapterRewriteMade.Failed($"nothing tells this process where output root '{job.OutputRoot.Value}' is mounted");
        }

        if (chapters is not null)
        {
            await ChapterMetadataFile.WriteAsync(chapters, wanted, TimeSpan.Zero, cancellationToken);
        }

        ProgrammeSaid copied = await AnotherProgramme.SayAsync(
            machine.Programme,
            FfmpegChapterRewriteInvocation.Arguments(artefact, chapters, rewritten),
            LongestCopy,
            clock,
            cancellationToken);

        if (copied.ExitCode is not 0)
        {
            return ChapterRewriteMade.Failed(string.Create(
                CultureInfo.InvariantCulture,
                $"the copy ended with {copied.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? copied.Fault?.ToString()}: {copied.Complained}"));
        }

        return await CheckedAsync(artefact, rewritten, wanted, cancellationToken);
    }

    private async Task<ChapterRewriteMade> CheckedAsync(
        string artefact,
        string rewritten,
        IReadOnlyList<ChapterSegment> wanted,
        CancellationToken cancellationToken)
    {
        CaptionTrackCarried? source = await CarriedAsync(artefact, cancellationToken);

        if (source is null)
        {
            return ChapterRewriteMade.Failed("what the artefact carries could not be read");
        }

        if (source.Subtitles.Count is not 0 && Mp4TrackFlags.TurnOffSubtitles(rewritten) != source.Subtitles.Count)
        {
            return ChapterRewriteMade.Failed("the copy did not carry the subtitle tracks the artefact carries to turn off by default");
        }

        CaptionTrackCarried? made = await CarriedAsync(rewritten, cancellationToken);
        IReadOnlyList<CarriedChapter>? chapters = await ChaptersAsync(rewritten, cancellationToken);

        if (made is null || chapters is null)
        {
            return ChapterRewriteMade.Failed("what the copy carries could not be read");
        }

        string? why = Differs(source, made) ?? ChaptersCarried.Differs(chapters, wanted);

        return why is null
            ? ChapterRewriteMade.Written(rewritten)
            : ChapterRewriteMade.Failed($"the copy is not the artefact with its chapters written again: {why}");
    }

    private static string? Differs(CaptionTrackCarried source, CaptionTrackCarried made)
    {
        if (made.Pictures != source.Pictures || made.Sounds != source.Sounds || !made.Subtitles.SequenceEqual(source.Subtitles))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"it carries {made.Pictures} picture, {made.Sounds} sound and {made.Subtitles.Count} subtitle track(s) where the artefact carries {source.Pictures}, {source.Sounds} and {source.Subtitles.Count}, or a subtitle track that is not the same");
        }

        return source.Length is not { } was || made.Length is not { } now || (now - was).Duration() > CaptionTrackCarried.Tolerance
            ? $"it lasts {made.Length?.ToString() ?? "an unread time"} where the artefact lasts {source.Length?.ToString() ?? "an unread time"}"
            : null;
    }

    private async Task<CaptionTrackCarried?> CarriedAsync(string file, CancellationToken cancellationToken)
    {
        ProgrammeSaid said = await AnotherProgramme.SayAsync(
            machine.Prober,
            FfmpegCaptionTrackInvocation.Carried(file),
            machine.LongestRead,
            clock,
            cancellationToken);

        return said.ExitCode is 0 ? CaptionTrackCarried.Read(said.Said) : null;
    }

    private async Task<IReadOnlyList<CarriedChapter>?> ChaptersAsync(string file, CancellationToken cancellationToken)
    {
        ProgrammeSaid said = await AnotherProgramme.SayAsync(
            machine.Prober,
            FfmpegChapterRewriteInvocation.Chapters(file),
            machine.LongestRead,
            clock,
            cancellationToken);

        return said.ExitCode is 0 ? ChaptersCarried.Read(said.Said) : null;
    }
}
