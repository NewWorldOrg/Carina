using Carina.Infrastructure.Auth;
using Carina.Infrastructure.Configuration;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.Auth;

public static class SealingKeys
{
    public static DirectoryInfo Fresh() => new(Path.Combine(
        Path.GetTempPath(),
        "carina-sealing-keys",
        Guid.NewGuid().ToString("N")));

    public static ClientSecretSeal Seal() => Seal(Fresh());

    public static ClientSecretSeal Seal(DirectoryInfo keys)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName(SealingKeyOptions.ApplicationName)
            .PersistKeysToFileSystem(keys);

        return new ClientSecretSeal(services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>());
    }
}
