using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public abstract record SoundOrigin;

public sealed record EntrySound(CatalogEntryId Entry) : SoundOrigin;

public sealed record RecordedSound(RecordingId Recording) : SoundOrigin;
