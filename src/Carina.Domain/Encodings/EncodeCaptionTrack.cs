namespace Carina.Domain.Encodings;

/// <summary>
/// What became of the text track of captions for an artefact, made from the captions taken from a recording.
/// </summary>
public enum EncodeCaptionTrack
{
    /// <summary>The artefact carries the text track made from that record.</summary>
    Added = 1,

    /// <summary>No track was put in: the captions taken hold no text, or the file they were taken from does not begin where the job's does.</summary>
    Withheld = 2,

    /// <summary>A track was to be put in and it could not be; the artefact is as it was.</summary>
    Failed = 3,
}
