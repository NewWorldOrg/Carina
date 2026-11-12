namespace Carina.Domain.Channels;

public sealed record WatchableChannel(BroadcastService Service, CandidateChannel Selected);
