using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// Where the sound a recording is matched against comes from: the fingerprint of a catalog entry,
/// or a recording.
/// </summary>
public abstract record SoundOrigin;

public sealed record EntrySound(CatalogEntryId Entry) : SoundOrigin;

public sealed record RecordedSound(RecordingId Recording) : SoundOrigin;
