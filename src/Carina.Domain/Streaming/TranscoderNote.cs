using Carina.Domain.Base;

namespace Carina.Domain.Streaming;

public static class TranscoderNote
{
    public const string InsteadOfAPath = ProgrammeNote.InsteadOfAPath;

    public const int Longest = ProgrammeNote.Longest;

    public static string Of(string said) => ProgrammeNote.Of(said, Longest);
}
