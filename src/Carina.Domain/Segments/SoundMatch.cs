namespace Carina.Domain.Segments;

/// <summary>
/// A stretch of the recording heard that sounds the same as a stretch of the same length in
/// <see cref="Other"/>, and the share of fingerprint bits on which the two disagree.
/// </summary>
public sealed record SoundMatch(SoundSpan Heard, SoundOrigin Other, SoundSpan There, double BitErrorRate);
