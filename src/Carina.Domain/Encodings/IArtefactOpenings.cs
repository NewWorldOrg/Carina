using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public interface IArtefactOpenings
{
    void Opened(OutputRoot root, string name);

    DateTimeOffset? LastOpened(OutputRoot root, string name);
}
