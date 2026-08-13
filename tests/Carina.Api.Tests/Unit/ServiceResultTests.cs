using Carina.Api.Common;

namespace Carina.Api.Tests.Unit;

public sealed class ServiceResultTests
{
    private enum SampleError
    {
        None,
        NotFound,
    }

    [Fact]
    public void SuccessCarriesNoError()
    {
        var result = ServiceResult.Success();

        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void FailureCarriesTheMessage()
    {
        var result = ServiceResult.Failure("broken");

        Assert.False(result.IsSuccess);
        Assert.Equal("broken", result.ErrorMessage);
    }

    [Fact]
    public void FailureRejectsABlankMessage()
    {
        Assert.Throws<ArgumentException>(() => ServiceResult.Failure(" "));
    }

    [Fact]
    public void TypedSuccessCarriesTheData()
    {
        var result = ServiceResult<string>.Success("payload");

        Assert.True(result.IsSuccess);
        Assert.Equal("payload", result.Data);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void TypedSuccessRejectsNullData()
    {
        Assert.Throws<ArgumentNullException>(() => ServiceResult<string>.Success(null!));
    }

    [Fact]
    public void TypedFailureCarriesNoData()
    {
        var result = ServiceResult<string>.Failure("broken");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Equal("broken", result.ErrorMessage);
    }

    [Fact]
    public void ErrorTypedFailureCarriesTheErrorKind()
    {
        var result = ServiceResult<string, SampleError>.Failure("missing", SampleError.NotFound);

        Assert.False(result.IsSuccess);
        Assert.Equal(SampleError.NotFound, result.ErrorType);
        Assert.Equal("missing", result.ErrorMessage);
    }

    [Fact]
    public void ErrorTypedSuccessLeavesTheErrorKindAtDefault()
    {
        var result = ServiceResult<string, SampleError>.Success("payload");

        Assert.True(result.IsSuccess);
        Assert.Equal(SampleError.None, result.ErrorType);
        Assert.Equal("payload", result.Data);
    }
}
