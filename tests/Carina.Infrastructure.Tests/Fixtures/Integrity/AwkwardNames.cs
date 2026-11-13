namespace Carina.Infrastructure.Tests.Fixtures.Integrity;

/// <summary>
/// Names a disk allows that a sweep must still read and carry as they are.
/// </summary>
internal static class AwkwardNames
{
    public static readonly string[] All =
    [
        "2026..08.m2ts",
        "..hidden.m2ts",
        "trailing.m2ts ",
        " leading.m2ts",
        "a\\b.m2ts",
        "-",
        "あ.m2ts",
        "a b  c.m2ts",
        "one'quote.m2ts",
        "one\"quote.m2ts",
        "percent%20.m2ts",
        ".hidden",
    ];
}
