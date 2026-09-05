using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Where, on this machine, a job writes and where its artefact ends up. The artefact goes under the
/// output root's mounted path; the work goes beside it unless a working directory is named, in
/// which case every root shares that one (A-エンコード-024 checks that the two are one mount).
/// </summary>
public sealed class EncodePlaces(IntegritySettings mounts, EncodeSettings settings)
{
    public bool WorksBesideTheArtefact => settings.WorkedIn is null;

    public string? WhereTheRootIs(OutputRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return mounts.OutputRoots.FirstOrDefault(mounted => mounted.Root.Equals(root))?.Path;
    }

    public string? WhereTheArtefactGoes(OutputRoot root) => WhereTheRootIs(root);

    public string? WhereTheWorkGoes(OutputRoot root) => settings.WorkedIn ?? WhereTheArtefactGoes(root);
}
