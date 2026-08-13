namespace Carina.Driver.Sessions;

public enum SessionStopReason
{
    Running,
    Requested,
    EndTimeReached,
    DrainCapReached,
    DeviceFailed,
    RecordingFailed,
}
