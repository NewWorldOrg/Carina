using Carina.Infrastructure.Persistence;

namespace Carina.Infrastructure.Tests;

public sealed class UtcDateTimeConverterTests
{
    private static readonly UtcDateTimeConverter Converter = new();

    [Fact]
    public void WritesUtcValuesUnchanged()
    {
        var value = new DateTime(2026, 8, 13, 1, 2, 3, DateTimeKind.Utc);

        Assert.Equal(value, Converter.ConvertToProvider(value));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void RefusesToWriteNonUtcValues(DateTimeKind kind)
    {
        var value = new DateTime(2026, 8, 13, 1, 2, 3, kind);

        var exception = Assert.Throws<ArgumentException>(() => Converter.ConvertToProvider(value));

        Assert.Contains(kind.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsStoredValuesBackAsUtc()
    {
        var stored = new DateTime(2026, 8, 13, 1, 2, 3, DateTimeKind.Unspecified);

        var read = Assert.IsType<DateTime>(Converter.ConvertFromProvider(stored));

        Assert.Equal(DateTimeKind.Utc, read.Kind);
        Assert.Equal(stored.Ticks, read.Ticks);
    }
}
