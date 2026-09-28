using Carina.Domain.Scans;

namespace Carina.Api.Common;

public static class ScanIdText
{
    public const string Description = "A scan is named by a UUID, and never by one that is all zeroes.";

    public static ScanRunId? Read(Guid id) => id == Guid.Empty ? null : new ScanRunId(id);
}
