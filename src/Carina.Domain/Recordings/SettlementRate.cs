using Carina.Domain.Channels;

namespace Carina.Domain.Recordings;

public sealed record SettlementRate(ExpectedBitrate? Bitrate, TuningRefusal Refusal, bool OfEveryKind);
