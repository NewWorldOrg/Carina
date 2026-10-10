using Carina.Domain.Channels;

namespace Carina.Domain.DataBroadcast;

public interface IDataBroadcastTaker
{
    /// <summary>
    /// Reads the sections of one service's data broadcast out of a recording's file from its start to its end, and
    /// gathers them into the record of it told on the clock the recording's captions are told on.
    /// </summary>
    Task<DataBroadcastTaking> TakeAsync(string source, ServiceId service, CancellationToken cancellationToken);
}
