using System.Text.Json.Serialization;

namespace Carina.Contracts;

[JsonConverter(typeof(SessionStopReasonConverter))]
public enum SessionStopReason
{
    Unspecified = 0,

    Running = 1,

    Requested = 2,

    EndTimeReached = 3,

    DrainCapReached = 4,

    DeviceFailed = 5,

    RecordingFailed = 6,
}
