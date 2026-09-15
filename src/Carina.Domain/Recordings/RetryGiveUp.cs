namespace Carina.Domain.Recordings;

public enum RetryGiveUp
{
    NotTransient = 1,

    PrecheckFailed = 2,

    CandidateNeedsAttention = 3,

    BroadcastOver = 4,

    AttemptsSpent = 5,
}
