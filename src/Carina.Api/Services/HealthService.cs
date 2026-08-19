using Carina.Api.Common;
using Carina.Domain.Auth;

namespace Carina.Api.Services;

public sealed class HealthService(IOidcReachability reachability)
{
    public ServiceResult<HealthView> Read()
        => ServiceResult<HealthView>.Success(HealthView.Of(reachability.State));
}
