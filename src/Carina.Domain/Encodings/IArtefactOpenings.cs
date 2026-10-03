using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// When each file handed out whole for somebody to watch was last opened, kept for as long as this process
/// runs, so that an artefact is not replaced under somebody who is still reading it.
/// </summary>
public interface IArtefactOpenings
{
    void Opened(OutputRoot root, string name);

    /// <summary>
    /// When the file was last opened for reading, or null when it was not since this process came up.
    /// </summary>
    DateTimeOffset? LastOpened(OutputRoot root, string name);
}
