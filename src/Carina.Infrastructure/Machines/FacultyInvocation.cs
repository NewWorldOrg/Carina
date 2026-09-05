namespace Carina.Infrastructure.Machines;

public static class FacultyInvocation
{
    public static IReadOnlyList<string> Encoders() => Listing("-encoders");

    public static IReadOnlyList<string> Decoders() => Listing("-decoders");

    private static IReadOnlyList<string> Listing(string asking)
        =>
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            asking,
        ];
}
