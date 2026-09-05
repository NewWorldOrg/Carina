using Carina.Broadcast.Descriptors;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Descriptors;

public sealed class LogoTransmissionTests
{
    private const int SomeLogoId = 261;
    private const int SomeLogoVersion = 3;
    private const int SomeDownloadDataId = 0x1234;

    [Fact]
    public void AStationThatKeepsItsLogoInTheCommonDataTableNamesTheLogoAndTheVersionItIsAt()
    {
        LogoTransmission.InTheCommonDataTable carried = Carried(
            SiDescriptorWriter.LogoInTheCommonDataTable(SomeLogoId, SomeLogoVersion, SomeDownloadDataId));

        Assert.Equal(SomeLogoId, carried.LogoId);
        Assert.Equal(SomeLogoVersion, carried.LogoVersion);
        Assert.Equal(SomeDownloadDataId, carried.DownloadDataId);
    }

    [Fact]
    public void AStationThatOnlyNamesItsLogoStillNamesTheSameLogoWithNothingBeside()
    {
        LogoTransmission.InTheCommonDataTable carried = Carried(SiDescriptorWriter.LogoNamedOnly(SomeLogoId));

        Assert.Equal(SomeLogoId, carried.LogoId);
        Assert.Null(carried.LogoVersion);
        Assert.Null(carried.DownloadDataId);
    }

    [Fact]
    public void ALogoIdAboveTheEighthBitSurvivesTheSevenReservedBitsBesideIt()
    {
        Assert.Equal(511, Carried(SiDescriptorWriter.LogoNamedOnly(511)).LogoId);
    }

    [Fact]
    public void AStationThatSendsAStringInsteadOfAPictureIsReadAsHavingNoPictureRatherThanARefusal()
    {
        LogoTransmission.ACharacterStringInstead instead = Assert.IsType<LogoTransmission.ACharacterStringInstead>(
            Read(SiDescriptorWriter.LogoAsACharacterString(new AribTextWriter().Kanji("試験").ToArray())));

        Assert.Equal("試験", instead.Text);
    }

    [Fact]
    public void AStationThatSendsAnEmptyStringInsteadOfAPictureIsStillAStationWithNoPicture()
    {
        Assert.IsType<LogoTransmission.ACharacterStringInstead>(Read(SiDescriptorWriter.LogoAsACharacterString([])));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x04)]
    [InlineData(0xFF)]
    public void AWayOfSendingALogoTheStandardDoesNotNameIsRefusedRatherThanGuessed(int transmissionType)
    {
        Assert.False(LogoTransmission.TryRead(
            Only(SiDescriptorWriter.LogoOfAnUnknownKind(transmissionType)),
            out LogoTransmission? _));
    }

    [Fact]
    public void ADescriptorWithNothingInItAtAllIsRefused()
    {
        Assert.False(LogoTransmission.TryRead(
            Only(DescriptorWriter.Of(DescriptorTags.LogoTransmission)),
            out LogoTransmission? _));
    }

    [Theory]
    [InlineData(0x01, LogoTransmission.WithDownloadDataIdLength)]
    [InlineData(0x02, LogoTransmission.SimpleLength)]
    public void ADescriptorCutShortOfWhatItsKindDeclaresIsRefusedRatherThanHalfRead(
        int transmissionType,
        int wholeLength)
    {
        byte[] whole = new byte[wholeLength];
        whole[0] = (byte)transmissionType;

        Assert.False(LogoTransmission.TryRead(
            Only(DescriptorWriter.Of(DescriptorTags.LogoTransmission, whole[..^1])),
            out LogoTransmission? _));
    }

    [Fact]
    public void ADescriptorWithMoreBytesThanItsKindDeclaresIsRefusedRatherThanTrusted()
    {
        Assert.False(LogoTransmission.TryRead(
            Only(DescriptorWriter.Of(DescriptorTags.LogoTransmission, 0x02, 0xFE, 0x05, 0x00)),
            out LogoTransmission? _));
    }

    [Fact]
    public void ADescriptorWithAnotherTagIsNotReadAsALogoTransmission()
    {
        Assert.False(LogoTransmission.TryRead(
            Only(SiDescriptorWriter.PartialReception(1)),
            out LogoTransmission? _));
    }

    private static LogoTransmission.InTheCommonDataTable Carried(byte[] descriptor)
        => Assert.IsType<LogoTransmission.InTheCommonDataTable>(Read(descriptor));

    private static LogoTransmission Read(byte[] descriptor)
    {
        Assert.True(LogoTransmission.TryRead(Only(descriptor), out LogoTransmission? transmission));

        return transmission;
    }

    private static Descriptor Only(byte[] descriptor)
    {
        Assert.True(DescriptorLoop.TryRead(descriptor, out IReadOnlyList<Descriptor>? descriptors));

        return Assert.Single(descriptors);
    }
}
