using System.Text;
using System.Text.Json;

namespace Carina.Contracts.Tests;

public sealed class StreamReadingTests
{
    private static async Task<T?> ReadOverAStreamAsync<T>(
        string json,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo
    )
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await JsonSerializer.DeserializeAsync(stream, typeInfo);
    }

    private static string Padded(string enumToken)
    {
        string detail = new string('x', 64 * 1024);
        return $$"""
            {"deviceId":"a0","kind":{{enumToken}},"state":"idle","sessionId":null,"detail":"{{detail}}"}
            """;
    }

    [Theory]
    [InlineData("\"someFutureKind\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{\"band\":\"bs\"}")]
    [InlineData("[\"bs\"]")]
    public async Task AValueThisBuildDoesNotKnowStillReadsOverAStream(string enumToken)
    {
        TunerSnapshot? tuner = await ReadOverAStreamAsync(
            Padded(enumToken),
            DriverJson.Context.TunerSnapshot
        );

        Assert.NotNull(tuner);
        Assert.Equal(TunerKind.Unspecified, tuner.Kind);
        Assert.Equal(TunerState.Idle, tuner.State);
        Assert.Equal("a0", tuner.DeviceId);
    }

    [Theory]
    [InlineData("\"../x\"")]
    [InlineData("123")]
    [InlineData("null")]
    [InlineData("{\"v\":\"x\"}")]
    [InlineData("[\"x\"]")]
    public async Task AnIdentifierThisBuildCannotTakeStillReadsOverAStream(string idToken)
    {
        string detail = new string('x', 64 * 1024);
        string json =
            $$"""
            {"deviceId":"a0","kind":"terrestrial","state":"busy","sessionId":{{idToken}},"detail":"{{detail}}"}
            """;

        TunerSnapshot? tuner = await ReadOverAStreamAsync(json, DriverJson.Context.TunerSnapshot);

        Assert.NotNull(tuner);
        Assert.True(tuner.SessionId.IsUnset);
        Assert.Equal(TunerState.Busy, tuner.State);
    }

    [Fact]
    public async Task AListReadsOverAStream()
    {
        string json = $"[{Padded("\"terrestrial\"")},{Padded("\"satellite\"")}]";

        IReadOnlyList<TunerSnapshot>? tuners = await ReadOverAStreamAsync(
            json,
            DriverJson.Context.IReadOnlyListTunerSnapshot
        );

        Assert.NotNull(tuners);
        Assert.Equal(2, tuners.Count);
        Assert.Equal(TunerKind.Satellite, tuners[1].Kind);
    }
}
