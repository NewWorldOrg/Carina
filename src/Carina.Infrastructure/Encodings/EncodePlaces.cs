using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Where, on this machine, a job reads and writes. The recording is read from under the root the
/// driver wrote it into; the artefact goes under one of the roots this process holds for writing,
/// and the work goes beside it unless a working directory is named for every root.
/// </summary>
public sealed class EncodePlaces(IntegritySettings mounts, EncodeSettings settings)
{
    public bool WorksBesideTheArtefact => settings.WorkedIn is null;

    public IReadOnlyList<OutputRoot> Held => [.. settings.OutputRoots.Select(held => held.Root)];

    public string? WhereTheRecordingIs(OutputRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return mounts.OutputRoots.FirstOrDefault(mounted => mounted.Root.Equals(root))?.Path;
    }

    public string? WhereTheArtefactGoes(OutputRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return settings.OutputRoots.FirstOrDefault(held => held.Root.Equals(root))?.Path;
    }

    public string? WhereTheWorkGoes(OutputRoot root) => settings.WorkedIn ?? WhereTheArtefactGoes(root);
}
