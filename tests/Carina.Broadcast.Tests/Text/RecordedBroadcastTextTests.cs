using System.Globalization;
using System.Reflection;
using System.Text;

using Carina.Broadcast.Text;

namespace Carina.Broadcast.Tests.Text;

public sealed class RecordedBroadcastTextTests
{
    public static TheoryData<string, string> Recorded()
    {
        var data = new TheoryData<string, string>();

        foreach ((string? bytes, string? expected) in Samples())
        {
            data.Add(bytes, expected);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Recorded))]
    public void ATitleTakenOffTheAirReadsBackAsItWasBroadcast(string bytes, string expected)
        => Assert.Equal(expected, AribText.Decode(Bytes(bytes)));

    [Fact]
    public void EveryRecordedTitleIsFullyMapped()
    {
        (string Bytes, string Expected)[] unreadable = Samples()
            .Where(sample => sample.Expected.Contains(AribText.UnknownCharacter, StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(unreadable);
    }

    [Fact]
    public void TheRecordedTitlesCarryTheSymbolsOnlyBroadcastUses()
    {
        int enclosing = Samples()
            .SelectMany(sample => sample.Expected.EnumerateRunes())
            .Count(rune => rune.Value is (>= 0x1F200 and <= 0x1F2FF) or (>= 0x3200 and <= 0x32FF));

        Assert.True(enclosing >= 10, $"expected the recording to exercise the symbol rows, saw {enclosing}");
    }

    private static byte[] Bytes(string hex)
    {
        byte[] bytes = new byte[hex.Length / 2];

        for (int at = 0; at < bytes.Length; at++)
        {
            bytes[at] = byte.Parse(hex.AsSpan(at * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return bytes;
    }

    private static IReadOnlyList<(string Bytes, string Expected)> Samples()
    {
        using Stream carried = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Carina.Broadcast.Tests.Text.Broadcasts.eit-text-samples.tsv")
            ?? throw new InvalidOperationException("The recorded broadcast text is missing from the test assembly.");

        using var reader = new StreamReader(carried, new UTF8Encoding(false));

        var samples = new List<(string, string)>();

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            int split = line.IndexOf('\t', StringComparison.Ordinal);

            samples.Add((line[..split], line[(split + 1)..]));
        }

        return samples;
    }
}
