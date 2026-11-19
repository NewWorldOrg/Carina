using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Encodings;

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
