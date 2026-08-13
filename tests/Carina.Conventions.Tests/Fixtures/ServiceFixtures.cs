using Carina.Api.Common;

namespace Carina.Conventions.Tests.Fixtures.Services;

internal sealed class RogueService
{
    public string Describe() => string.Empty;
}

internal sealed class CompliantService
{
    public ServiceResult Complete() => ServiceResult.Success();

    public Task<ServiceResult<int>> CountAsync() => Task.FromResult(ServiceResult<int>.Success(1));
}
