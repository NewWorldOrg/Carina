namespace Carina.Infrastructure.Streaming;

public sealed class FfprobeRecord
{
    private readonly Dictionary<string, string> fields = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => fields.Keys;

    public string? Value(string key) => fields.TryGetValue(key, out string? value) ? value : null;

    public bool Holds(string key) => fields.ContainsKey(key);

    internal void Add(string key, string value) => fields[key] = value;
}

public static class FfprobeRecords
{
    public static IReadOnlyList<FfprobeRecord> From(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        List<FfprobeRecord> records = [];
        FfprobeRecord? current = null;

        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.Trim('\r', ' ', '\t');
            int equals = trimmed.IndexOf('=', StringComparison.Ordinal);

            if (equals < 1)
            {
                continue;
            }

            string key = trimmed[..equals];
            string value = trimmed[(equals + 1)..];

            if (current is null || current.Holds(key))
            {
                current = new FfprobeRecord();
                records.Add(current);
            }

            current.Add(key, value);
        }

        return records;
    }
}
