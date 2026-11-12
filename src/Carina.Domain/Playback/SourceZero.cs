namespace Carina.Domain.Playback;

/// <summary>
/// Where the zero of a source a recording is played from sits on the clock of the recording's file, and how long
/// that source lasts where it says so.
/// </summary>
public sealed record SourceZero(TimeSpan Shift, TimeSpan? Length);
