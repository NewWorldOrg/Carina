namespace Carina.Domain.Channels;

/// <summary>
/// Whether a tuner in service receives the system a service is tuned on.
/// </summary>
public enum ServiceReception
{
    Receivable = 1,

    NoTunerInService = 2,

    Unknown = 3,
}
