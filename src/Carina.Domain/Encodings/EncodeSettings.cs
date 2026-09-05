namespace Carina.Domain.Encodings;

/// <summary>
/// Where a job writes while it works. Left unset, a work file is written beside the artefact it
/// will become, under the same output root, so the rename that finishes the job never crosses a
/// mount. Set, it names one directory for every root, and the check at startup refuses a directory
/// on another mount than any root (A-エンコード-024).
/// </summary>
public sealed record EncodeSettings
{
    public string? WorkedIn { get; init; }
}
