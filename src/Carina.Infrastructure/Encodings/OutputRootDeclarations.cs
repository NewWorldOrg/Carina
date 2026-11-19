using Carina.Domain.Driver;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Infrastructure.Recordings;

using Microsoft.Extensions.Logging;

using Puppis.Contracts;

namespace Carina.Infrastructure.Encodings;

public sealed class OutputRootDeclarations(
    StorageMonitor driver,
    EncodeSettings settings,
    IRenameProbe probe,
    ILogger<OutputRootDeclarations> logger)
{
    public async Task<DriverCall<IReadOnlyList<StorageRootDto>>> ReadAsync(CancellationToken cancellationToken)
    {
        DriverCall<IReadOnlyList<StorageRootDto>> answer = await driver.ReadAsync(cancellationToken);

        if (!answer.TryGetValue(out IReadOnlyList<StorageRootDto>? declared))
        {
            return answer;
        }

        DeclaredOutputRoots merged = EncodeRootDeclarations.Merged(declared, [.. settings.OutputRoots.Select(Measured)]);

        foreach (string shadowed in merged.Shadowed)
        {
            logger.LogWarning(
                "Encode root {Root} has the name of a root the driver declares, so it is left out of the declared set and nothing can be encoded into it.",
                shadowed);
        }

        return DriverCall<IReadOnlyList<StorageRootDto>>.Reached(merged.Declared);
    }

    private StorageRootDto Measured(StorageRootPath held)
    {
        try
        {
            var room = new DriveInfo(held.Path);

            return new StorageRootDto
            {
                Name = held.Root.Value,
                FreeBytes = room.AvailableFreeSpace,
                TotalBytes = room.TotalSize,
                Writable = probe.Probe(held.Path, held.Path).IsARename,
            };
        }
        catch (Exception unmeasured) when (unmeasured is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new StorageRootDto { Name = held.Root.Value };
        }
    }
}
