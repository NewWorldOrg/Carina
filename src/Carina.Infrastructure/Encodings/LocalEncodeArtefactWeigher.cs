using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

public sealed class LocalEncodeArtefactWeigher(
    EncodePlaces places,
    ILogger<LocalEncodeArtefactWeigher> logger) : IEncodeArtefactWeigher
{
    public long? Weigh(OutputRoot root, EncodeFileName name)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(name);

        if (places.WhereTheArtefactGoes(root) is not { } room)
        {
            return null;
        }

        try
        {
            FileInfo found = new(Path.Combine(room, name.Value));

            return found.Exists ? found.Length : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "The artefact {File} under output root {Root} could not be read off the disk.",
                name.Value,
                root.Value);

            return null;
        }
    }
}
