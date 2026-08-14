using System.Text;

using Carina.Broadcast.Sections;
using Carina.Broadcast.Tests.Building;

namespace Carina.Broadcast.Tests.Sections;

public sealed class Crc32MpegTests
{
    [Fact]
    public void TheTableDrivenRegisterAgreesWithTheBitwiseModelOnTheCheckString()
    {
        var check = Encoding.ASCII.GetBytes("123456789");

        Assert.Equal(0x0376_E6E7u, Crc32Mpeg.Compute(check));
        Assert.Equal(0x0376_E6E7u, ReferenceCrc32.Compute(check));
    }

    [Fact]
    public void TheTableDrivenRegisterAgreesWithTheBitwiseModelOnArbitraryLengths()
    {
        var random = new Random(20260814);

        for (var length = 0; length < 300; length++)
        {
            var data = new byte[length];
            random.NextBytes(data);

            Assert.Equal(ReferenceCrc32.Compute(data), Crc32Mpeg.Compute(data));
        }
    }

    [Fact]
    public void AnEmptyRunLeavesTheSeedUntouched()
    {
        Assert.Equal(0xFFFF_FFFFu, Crc32Mpeg.Compute([]));
    }

    [Fact]
    public void DataFollowedByItsOwnChecksumLeavesNoResidue()
    {
        var data = new byte[] { 0x40, 0xF0, 0x11, 0x00, 0x01, 0xC1, 0x00, 0x00 };
        var checksum = Crc32Mpeg.Compute(data);

        var carried = new byte[data.Length + 4];
        data.CopyTo(carried, 0);
        carried[^4] = (byte)(checksum >> 24);
        carried[^3] = (byte)(checksum >> 16);
        carried[^2] = (byte)(checksum >> 8);
        carried[^1] = (byte)checksum;

        Assert.True(Crc32Mpeg.Verifies(carried));
    }

    [Fact]
    public void ASingleFlippedBitBreaksTheResidue()
    {
        var data = new byte[] { 0x42, 0xF0, 0x20, 0x00, 0x01, 0xC1, 0x00, 0x00 };
        var checksum = Crc32Mpeg.Compute(data);

        var carried = new byte[data.Length + 4];
        data.CopyTo(carried, 0);
        carried[^4] = (byte)(checksum >> 24);
        carried[^3] = (byte)(checksum >> 16);
        carried[^2] = (byte)(checksum >> 8);
        carried[^1] = (byte)checksum;
        carried[2] ^= 0x01;

        Assert.False(Crc32Mpeg.Verifies(carried));
    }

    [Fact]
    public void TooFewBytesToHoldAChecksumDoNotVerify()
    {
        Assert.False(Crc32Mpeg.Verifies([0x00, 0x01, 0x02]));
    }
}
