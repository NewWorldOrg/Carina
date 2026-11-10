using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public interface IEncodeArtefactWeigher
{
    /// <summary>
    /// How many bytes the artefact under the root holds, or <see langword="null"/> when it cannot be read.
    /// </summary>
    long? Weigh(OutputRoot root, EncodeFileName name);
}
