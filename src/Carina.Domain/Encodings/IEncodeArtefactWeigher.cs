using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public interface IEncodeArtefactWeigher
{
    long? Weigh(OutputRoot root, EncodeFileName name);
}
