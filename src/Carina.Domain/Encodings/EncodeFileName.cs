using System.Globalization;

using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// The name of a file this domain writes under an output root. A work file is named for the
/// recording, the job and the attempt, so two jobs on one recording, or two attempts of one job,
/// cannot write into the same file; the artefact is named for the recording and the profile, and
/// for nothing a broadcaster wrote (BR-ED2-009).
/// </summary>
public sealed class EncodeFileName : CommonValueObject<string>
{
    public const int MaxLength = RecordingFileName.MaxLength;

    public const string WorkExtension = ".encoding";

    public const string ArtefactExtension = ".mp4";

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
