using System.Reflection;

using Carina.Api.Common;

namespace Carina.Api.Services;

public sealed record VersionView(string Version)
{
    public static VersionView Of(Assembly assembly) => new(DeclaredVersion.Of(assembly));
}

public sealed class VersionService
{
    public ServiceResult<VersionView> Read()
        => ServiceResult<VersionView>.Success(VersionView.Of(typeof(VersionService).Assembly));
}
