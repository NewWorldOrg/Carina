namespace Carina.Driver.Tests;

public sealed class InvariantTablesTests
{
    [Fact]
    public void TheDriverNormalisesNothingAndItsTestsRunOnTheSameTablesItDoes()
    {
        Assert.True(
            AppContext.TryGetSwitch("System.Globalization.Invariant", out bool held) && held,
            "the driver keeps the invariant tables, and its tests have to run on them too or they measure another process");
        Assert.Equal("ﾆｭｰｽ", "ﾆｭｰｽ".Normalize(System.Text.NormalizationForm.FormKC));
    }
}
