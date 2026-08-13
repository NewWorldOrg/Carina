using System.Runtime.CompilerServices;
using System.Text;

namespace Carina.Infrastructure.Driver;

public static class SseFrames
{
    private const string EventField = "event:";

    public static async IAsyncEnumerable<string> ReadNamesAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);

        string? name = null;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    yield return name;
                }

                name = null;

                continue;
            }

            if (line.StartsWith(EventField, StringComparison.Ordinal))
            {
                name = line[EventField.Length..].Trim();
            }
        }
    }
}
