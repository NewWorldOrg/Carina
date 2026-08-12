using System.Text;
using System.Text.Json;

namespace Carina.Contracts.Tests;

/// <summary>
/// The same messages, read the way the socket reads them.
/// </summary>
/// <remarks>
/// A body arrives over a stream, in buffer-sized pieces. Anything that only works
/// when the whole document is in hand — skipping a token, for one — fails there and
/// nowhere else, so the tolerance the contract promises has to be proven on this
/// path and not just on a string. The padding pushes the message past the reader's
/// default buffer so the interesting token lands in a partial block.
/// </remarks>
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
        var detail = new string('x', 64 * 1024);
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
        var tuner = await ReadOverAStreamAsync(
            Padded(enumToken),
            DriverJson.Context.TunerSnapshot
        );

        Assert.NotNull(tuner);
        Assert.Equal(TunerKind.Unspecified, tuner.Kind);
        Assert.Equal(TunerState.Idle, tuner.State);
        Assert.Equal("a0", tuner.DeviceId);
    }

    // Same rule for the identifier: a shape this build cannot take must not cost the
    // message it sits in, whether it arrives as a scalar or as a structure.
    [Theory]
    [InlineData("\"../x\"")]
    [InlineData("123")]
    [InlineData("null")]
    [InlineData("{\"v\":\"x\"}")]
    [InlineData("[\"x\"]")]
    public async Task AnIdentifierThisBuildCannotTakeStillReadsOverAStream(string idToken)
    {
        var detail = new string('x', 64 * 1024);
        var json =
            $$"""
            {"deviceId":"a0","kind":"terrestrial","state":"busy","sessionId":{{idToken}},"detail":"{{detail}}"}
            """;

        var tuner = await ReadOverAStreamAsync(json, DriverJson.Context.TunerSnapshot);

        Assert.NotNull(tuner);
        Assert.True(tuner.SessionId.IsUnset);
        Assert.Equal(TunerState.Busy, tuner.State);
    }

    [Fact]
    public async Task AListReadsOverAStream()
    {
        var json = $"[{Padded("\"terrestrial\"")},{Padded("\"satellite\"")}]";

        var tuners = await ReadOverAStreamAsync(
            json,
            DriverJson.Context.IReadOnlyListTunerSnapshot
        );

        Assert.NotNull(tuners);
        Assert.Equal(2, tuners.Count);
        Assert.Equal(TunerKind.Satellite, tuners[1].Kind);
    }
}
