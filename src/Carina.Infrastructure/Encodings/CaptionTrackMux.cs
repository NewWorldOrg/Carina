using System.Globalization;
using System.Text;

using Carina.Domain.Captions;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// What putting a text track of captions into an artefact came to: the file written with the track put in,
/// when it was, and why not otherwise.
/// </summary>
public sealed record CaptionTrackMade(EncodeCaptionTrack Outcome, string? Captioned, string Note)
{
    public static CaptionTrackMade Added(string captioned) => new(EncodeCaptionTrack.Added, captioned, string.Empty);

    public static CaptionTrackMade Withheld(string note) => new(EncodeCaptionTrack.Withheld, null, note);

    public static CaptionTrackMade Failed(string note) => new(EncodeCaptionTrack.Failed, null, note);
}

/// <summary>
/// The names the text of captions and the copy with the track put in are written under: the job's own for
/// the attempt that is making the artefact, and names that also carry a try of their own for an artefact
/// that stands already, which may be tried more than once.
/// </summary>
public sealed record CaptionTrackNames(EncodeFileName Captions, EncodeFileName Captioned)
{
    public static CaptionTrackNames Making(EncodeJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new CaptionTrackNames(job.CaptionTrackFileName, job.CaptionedFileName);
    }

    public static CaptionTrackNames Tried(EncodeJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        EncodeScratchFileId tried = EncodeScratchFileId.New();

        return new CaptionTrackNames(
            EncodeFileName.CaptionTrack(job.RecordingId, job.Id, job.Attempt, tried),
            EncodeFileName.Captioned(job.RecordingId, job.Id, job.Attempt, tried));
    }
}

/// <summary>
/// Writes a copy of an artefact with the text of a recording's captions put in as a text track, beside the
/// work of the job that made the artefact: the text and the copy are written into the ledger as scratch first,
/// the copy's subtitle track is turned off by default, and the copy is checked against the artefact before it
/// is handed back. The artefact itself is only read.
/// </summary>
public sealed class CaptionTrackMux(EncodeScratchFiles scratch, MachineSettings machine, TimeProvider clock)
{
    public static readonly TimeSpan LongestCopy = TimeSpan.FromHours(1);

    public static readonly TimeSpan StartsAgreeWithin = TimeSpan.FromMilliseconds(1);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<CaptionTrackMade> MakeAsync(
        EncodeJob job,
        string artefact,
        CaptionRecord record,
        CaptionTrackNames names,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrEmpty(artefact);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(names);

        if (record.Lines is not { } lines)
        {
            throw new ArgumentException("A text track is made from a record that carries the text of its captions.", nameof(record));
        }

        if (Unmade(job, record, lines, out string? text) is { } unmade)
        {
            return unmade;
        }

        string? captions = await scratch.RecordAsync(job, EncodeScratchKind.Captions, names.Captions, cancellationToken);
        string? captioned = captions is null
            ? null
            : await scratch.RecordAsync(job, EncodeScratchKind.CaptionedWork, names.Captioned, cancellationToken);

        if (captions is null || captioned is null)
        {
            return CaptionTrackMade.Failed($"nothing tells this process where output root '{job.OutputRoot.Value}' is mounted");
        }

        await File.WriteAllTextAsync(captions, text, Utf8, cancellationToken);

        ProgrammeSaid copied = await AnotherProgramme.SayAsync(
            machine.Programme,
            FfmpegCaptionTrackInvocation.Arguments(artefact, captions, captioned),
            LongestCopy,
            clock,
            cancellationToken);

        if (copied.ExitCode is not 0)
        {
            return CaptionTrackMade.Failed(string.Create(
                CultureInfo.InvariantCulture,
                $"the copy ended with {copied.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? copied.Fault?.ToString()}: {copied.Complained}"));
        }

        if (Mp4TrackFlags.TurnOffSubtitles(captioned) is not 1)
        {
            return CaptionTrackMade.Failed("the copy carried no subtitle track to turn off by default");
        }

        return await CheckedAsync(artefact, captioned, cancellationToken);
    }

    private static CaptionTrackMade? Unmade(EncodeJob job, CaptionRecord record, IReadOnlyList<CaptionLine> lines, out string? text)
    {
        text = null;

        if (job.Timeline is not { } timeline)
        {
            return CaptionTrackMade.Withheld("the job never said where the artefact's clock begins");
        }

        if ((record.StartsAt - timeline.SourceStart).Duration() >= StartsAgreeWithin)
        {
            return CaptionTrackMade.Withheld(string.Create(
                CultureInfo.InvariantCulture,
                $"the captions were taken from a file whose clock began at {record.StartsAt.TotalSeconds:0.######} s and the job read {timeline.SourceStart.TotalSeconds:0.######} s"));
        }

        text = CaptionTrackFile.Written(lines, timeline.CaptionShift, timeline.ArtefactLength ?? timeline.Expected);

        return text is null ? CaptionTrackMade.Withheld("the record holds no text within the artefact") : null;
    }

    private async Task<CaptionTrackMade> CheckedAsync(string artefact, string captioned, CancellationToken cancellationToken)
    {
        CaptionTrackCarried? source = await CarriedAsync(artefact, cancellationToken);
        CaptionTrackCarried? made = await CarriedAsync(captioned, cancellationToken);

        if (source is null || made is null)
        {
            return CaptionTrackMade.Failed("what the artefact and the copy carry could not be read");
        }

        return CaptionTrackCarried.Differs(source, made) is { } why
            ? CaptionTrackMade.Failed($"the copy is not the artefact with one text track put in: {why}")
            : CaptionTrackMade.Added(captioned);
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
}
