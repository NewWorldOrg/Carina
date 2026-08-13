using Carina.Api.Responder;

namespace Carina.Api.Tests.Unit;

public sealed class BaseResponderTests
{
    [Fact]
    public void SuccessWrapsTheData()
    {
        var responder = BaseResponder<string>.Success("payload");

        Assert.True(responder.Status);
        Assert.Equal(string.Empty, responder.Message);
        Assert.Equal("payload", responder.Data);
    }

    [Fact]
    public void ErrorCarriesTheMessageWithoutData()
    {
        var responder = BaseResponder<string>.Error("broken");

        Assert.False(responder.Status);
        Assert.Equal("broken", responder.Message);
        Assert.Null(responder.Data);
    }
}
