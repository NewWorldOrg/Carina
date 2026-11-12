using Carina.Domain.Channels;

namespace Carina.Domain.Recordings;

/// <summary>
/// The rate a recording's weight is judged against as it ends, and why the catalogue named the channel it did: the
/// rate of the kind its selected channel is carried on, the range every kind falls in when the service or its
/// selected channel is gone, or none when the catalogue named no channel for another reason.
/// </summary>
public sealed record SettlementRate(ExpectedBitrate? Bitrate, TuningRefusal Refusal, bool OfEveryKind);
