using Carina.Domain.Channels;

namespace Carina.Domain.DataBroadcast;

public interface IDataBroadcastTaker
{
    Task<DataBroadcastTaking> TakeAsync(string source, ServiceId service, CancellationToken cancellationToken);
}
