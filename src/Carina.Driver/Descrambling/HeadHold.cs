namespace Carina.Driver.Descrambling;

/// <summary>
/// What the head of a stream is held for before any of it is handed on: every programme the PAT lists, or the one
/// programme a viewer waits for.
/// </summary>
public sealed record HeadHold
{
    public static readonly HeadHold EveryProgramme = new(programme: null);

    private HeadHold(int? programme) => Programme = programme;

    /// <summary>
    /// The programme number the head waits for alone, or null when it waits for every programme.
    /// </summary>
    public int? Programme { get; }

    public static HeadHold ForProgramme(int programme) => new(programme);
}
