namespace Carina.Infrastructure.Tests.Encodings;

public static class TextAShellWouldReadAgain
{
    public static TheoryData<string> Every
        => new()
        {
            "; rm -rf /",
            "`whoami`",
            "$(whoami)",
            "a\n-y\n-i\n/etc/passwd",
            "a\0-y",
            new string('x', 65536),
        };
}
