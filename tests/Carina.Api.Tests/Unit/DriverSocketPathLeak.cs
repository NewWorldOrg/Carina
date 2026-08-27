namespace Carina.Api.Tests.Unit;

public sealed record AnsweredSurface(string Surface, int Status, string Body);

public static class DriverSocketPathLeak
{
    public static IReadOnlyList<string> WhereItIsNamed(string socketPath) =>
    [
        socketPath,
        Path.GetDirectoryName(socketPath)!,
        Path.GetFileName(socketPath),
    ];

    public static IReadOnlyList<string> In(IEnumerable<AnsweredSurface> answered, string socketPath)
    {
        ArgumentNullException.ThrowIfNull(answered);
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);

        IReadOnlyList<string> named = WhereItIsNamed(socketPath);

        return
        [
            .. answered
                .Where(answer => named.Any(part => answer.Body.Contains(part, StringComparison.Ordinal)))
                .Select(answer => $"{answer.Surface} answered {answer.Status} naming where the socket is")
                .Order(StringComparer.Ordinal),
        ];
    }
}
