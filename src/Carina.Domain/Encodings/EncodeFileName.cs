using System.Globalization;

using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// The name of a file this domain writes under an output root. A work file, the chapters an attempt
/// reads, the text of captions a track is made from and the artefact with that track put in are named
/// for the recording, the job and the attempt; the artefact is named for the recording and the profile.
/// </summary>
public sealed class EncodeFileName : CommonValueObject<string>
{
    public const int MaxLength = RecordingFileName.MaxLength;

    public const string WorkExtension = ".encoding";

    public const string ChaptersExtension = ".chapters";

    public const string ArtefactExtension = ".mp4";

    public const string CaptionTrackExtension = ".vtt";

    public const string CaptionedExtension = ".captioned";

    private static readonly char[] Separators = ['/', '\\', '\0'];

    public EncodeFileName(string value)
        : base(Validated(value))
    {
    }

    public static EncodeFileName Working(RecordingId recording, EncodeJobId job, int attempt)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, EncodeJob.FirstAttempt);

        return new EncodeFileName(string.Create(
            CultureInfo.InvariantCulture,
            $"{recording.Wire}.{job.Wire}.attempt{attempt}{WorkExtension}"));
    }

    public static EncodeFileName Chapters(RecordingId recording, EncodeJobId job, int attempt)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, EncodeJob.FirstAttempt);

        return new EncodeFileName(string.Create(
            CultureInfo.InvariantCulture,
            $"{recording.Wire}.{job.Wire}.attempt{attempt}{ChaptersExtension}"));
    }

    public static EncodeFileName CaptionTrack(RecordingId recording, EncodeJobId job, int attempt)
        => Attempted(recording, job, attempt, CaptionTrackExtension);

    public static EncodeFileName Captioned(RecordingId recording, EncodeJobId job, int attempt)
        => Attempted(recording, job, attempt, CaptionedExtension);

    /// <summary>
    /// The text of captions a track is made from for an artefact that stands already, named also for the try,
    /// so that no two tries share a name.
    /// </summary>
    public static EncodeFileName CaptionTrack(RecordingId recording, EncodeJobId job, int attempt, EncodeScratchFileId tried)
        => Attempted(recording, job, attempt, Tried(tried) + CaptionTrackExtension);

    /// <summary>
    /// A standing artefact with a text track of captions put in, named also for the try.
    /// </summary>
    public static EncodeFileName Captioned(RecordingId recording, EncodeJobId job, int attempt, EncodeScratchFileId tried)
        => Attempted(recording, job, attempt, Tried(tried) + CaptionedExtension);

    public static EncodeFileName Artefact(RecordingId recording, EncodeProfileId profile)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(profile);

        return new EncodeFileName($"{recording.Wire}.{profile.Wire}{ArtefactExtension}");
    }

    public bool Names(RecordingId recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return Value.Contains(recording.Wire, StringComparison.Ordinal);
    }

    public bool Names(EncodeJobId job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return Value.Contains(job.Wire, StringComparison.Ordinal);
    }

    private static string Tried(EncodeScratchFileId tried)
    {
        ArgumentNullException.ThrowIfNull(tried);

        return string.Create(CultureInfo.InvariantCulture, $".try{tried.Value:N}");
    }

    private static EncodeFileName Attempted(RecordingId recording, EncodeJobId job, int attempt, string extension)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, EncodeJob.FirstAttempt);

        return new EncodeFileName(string.Create(
            CultureInfo.InvariantCulture,
            $"{recording.Wire}.{job.Wire}.attempt{attempt}{extension}"));
    }

    private static string Validated(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"A file name is at most {MaxLength} characters, but this one has {value.Length}.",
                nameof(value));
        }

        if (value.IndexOfAny(Separators) >= 0)
        {
            throw new ArgumentException("A file name is a single name, so it carries no separator.", nameof(value));
        }

        if (value.Contains("..", StringComparison.Ordinal) || value is ".")
        {
            throw new ArgumentException("A file name names a file, never the way out of its room.", nameof(value));
        }

        if (value.Trim().Length != value.Length)
        {
            throw new ArgumentException("A file name carries no surrounding space.", nameof(value));
        }

        foreach (char letter in value)
        {
            if (char.IsControl(letter))
            {
                throw new ArgumentException("A file name carries no control character.", nameof(value));
            }
        }

        return value;
    }
}
