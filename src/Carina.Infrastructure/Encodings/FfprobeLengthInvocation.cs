namespace Carina.Infrastructure.Encodings;

public static class FfprobeLengthInvocation
{
    public const string Format = "default=nw=1";

    public const string Entries = "format=duration";

    public static IReadOnlyList<string> Arguments(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        return
        [
            "-hide_banner",
            "-loglevel",
            "error",
            "-of",
            Format,
            "-show_entries",
            Entries,
            "-i",
            source,
        ];
    }
}
