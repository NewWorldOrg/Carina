namespace Carina.Domain.Integrity;

/// <summary>
/// Which kind of directory a sweep walks: an output root the driver writes recordings into, a root
/// this process writes encoded artefacts into, or the directory it draws thumbnails into.
/// </summary>
public enum StoragePlace
{
    Recordings = 1,

    Encodes = 2,

    Thumbnails = 3,
}
