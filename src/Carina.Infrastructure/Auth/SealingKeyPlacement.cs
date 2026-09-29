using Carina.Infrastructure.Configuration;

using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Auth;

public sealed class SealingKeyPlacement(IOptions<SealingKeyOptions> keys, ILoggerFactory logs)
    : IConfigureOptions<KeyManagementOptions>
{
    public void Configure(KeyManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(keys.Value.Directory!), logs);
    }
}
