using System.Text;

using Carina.Driver.Descrambling;

namespace Carina.Driver.Tests;

public sealed class SmartCardServiceTests
{
    [Fact]
    public void TheReaderListIsSplitAtEachZeroByteAndEndsAtTheEmptyName()
    {
        byte[] list = Encoding.UTF8.GetBytes("First Reader 00 00\0Second Reader 01 00\0\0");

        Assert.Equal(["First Reader 00 00", "Second Reader 01 00"], PcscLibrary.ReaderNames(list));
    }

    [Fact]
    public void AnEmptyListNamesNoReader()
    {
        Assert.Empty(PcscLibrary.ReaderNames([0]));
        Assert.Empty(PcscLibrary.ReaderNames([]));
    }

    [Fact]
    public void ANameLeftWithoutItsZeroByteIsStillRead()
    {
        Assert.Equal(["Only Reader"], PcscLibrary.ReaderNames(Encoding.UTF8.GetBytes("Only Reader")));
    }

    [Theory]
    [InlineData(SmartCardCodes.ResetCard, true)]
    [InlineData(SmartCardCodes.RemovedCard, true)]
    [InlineData(SmartCardCodes.NoSmartCard, false)]
    [InlineData(SmartCardCodes.SharingViolation, false)]
    public void OnlyAResetOrARemovedCardIsWorthReconnectingFor(uint code, bool reset)
    {
        Assert.Equal(reset, new SmartCardException("synthetic", code).CardWasReset);
    }

    [Theory]
    [InlineData(SmartCardCodes.NoService, "0x8010001D")]
    [InlineData(0x80100099, "0x80100099")]
    public void EveryCodeIsDescribedWithItsNumber(uint code, string number)
    {
        Assert.Contains(number, SmartCardCodes.Describe(code), StringComparison.Ordinal);
    }

    [Fact]
    public void TheLibraryIsEitherLoadedOrSaysWhyNot()
    {
        PcscLibrary? library = PcscLibrary.Load(out string whyNot);

        Assert.True(library is not null || whyNot.Contains(PcscLibrary.SharedObject, StringComparison.Ordinal));
    }
}
