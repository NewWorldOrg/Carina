namespace Carina.Domain.Channels;

/// <summary>
/// Why a channel is not added to a service as another candidate.
/// </summary>
public enum CandidateChannelRefusal
{
    AlreadyHeld = 1,

    DriverUnreachable = 2,

    NoTunerReceivesIt = 3,
}
