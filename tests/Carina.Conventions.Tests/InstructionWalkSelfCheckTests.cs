namespace Carina.Conventions.Tests;

public sealed class InstructionWalkSelfCheckTests
{
    private const int Token = 0x0A000001;

    private const byte LoadArgumentZero = 0x02;

    private const byte Switch = 0x45;

    private const int LoadLongConstant = 0x21;

    private const byte Call = 0x28;

    private const byte Return = 0x2A;

    [Fact]
    public void AJumpTableIsAsWideAsTheCountItCarriesSaysItIs()
    {
        IReadOnlyList<int> tokens = CallSiteCensus.TokensIn(WithAJumpTableOf(1), nameof(AJumpTableIsAsWideAsTheCountItCarriesSaysItIs));

        Assert.Equal([Token], tokens);
    }

    [Fact]
    public void EveryEntryInTheJumpTableIsStepped()
    {
        Assert.Equal([Token], CallSiteCensus.TokensIn(WithAJumpTableOf(4), nameof(EveryEntryInTheJumpTableIsStepped)));
        Assert.Equal([Token], CallSiteCensus.TokensIn(WithAJumpTableOf(9), nameof(EveryEntryInTheJumpTableIsStepped)));
    }

    [Fact]
    public void AWalkThatRunsPastTheEndOfABodyRefusesToAnswer()
    {
        byte[] truncated = [LoadArgumentZero, Call, 0x01, 0x00];

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => CallSiteCensus.TokensIn(truncated, "a body cut off mid operand"));

        Assert.Contains("runs past the end", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWalkThatMeetsSomethingThatIsNotAnOpcodeRefusesToAnswer()
    {
        byte[] nonsense = [0xFE, 0x7F, Return];

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => CallSiteCensus.TokensIn(nonsense, "a body carrying no such opcode"));

        Assert.Contains("not an opcode", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AJumpTableWhoseCountRunsPastTheBodyRefusesToAnswer()
    {
        byte[] overlong = [LoadArgumentZero, Switch, 0xFF, 0x00, 0x00, 0x00, Return];

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => CallSiteCensus.TokensIn(overlong, "a jump table longer than its body"));

        Assert.Contains("runs past the end", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AJumpTableThatCountsBackwardsRefusesToAnswer()
    {
        byte[] backwards = [LoadArgumentZero, Switch, .. BitConverter.GetBytes(-1), Return];

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => CallSiteCensus.TokensIn(backwards, "a jump table counting backwards"));

        Assert.Contains("is not a count", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWalkOverNothingFindsNothingRatherThanRefusing()
        => Assert.Empty(CallSiteCensus.TokensIn([], "an empty body"));

    private static byte[] WithAJumpTableOf(int entries)
    {
        List<byte> il = [LoadArgumentZero, Switch, .. BitConverter.GetBytes(entries)];

        for (int entry = 0; entry < entries; entry++)
        {
            il.AddRange(BitConverter.GetBytes(LoadLongConstant));
        }

        il.Add(Call);
        il.AddRange(BitConverter.GetBytes(Token));
        il.Add(Return);

        return [.. il];
    }
}
