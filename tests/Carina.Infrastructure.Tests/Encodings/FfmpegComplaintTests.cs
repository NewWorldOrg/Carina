using Carina.Domain.Encodings;
using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class FfmpegComplaintTests
{
    [Theory(DisplayName = "BR-ED2-012: a disk that is full is its own reason, and everything else the programme refused")]
    [InlineData("av_interleaved_write_frame(): No space left on device", EncodeFailure.NotEnoughRoom)]
    [InlineData("Error writing trailer: no space left on device", EncodeFailure.NotEnoughRoom)]
    [InlineData("Invalid data found when processing input", EncodeFailure.FfmpegExitedNonZero)]
    [InlineData("", EncodeFailure.FfmpegExitedNonZero)]
    public void ADiskThatIsFullIsItsOwnReason(string complained, EncodeFailure expected)
        => Assert.Equal(expected, FfmpegComplaint.Classified(complained));
}
