namespace Carina.Domain.Segments;

public sealed record SoundMatch(SoundSpan Heard, SoundOrigin Other, SoundSpan There, double BitErrorRate);
