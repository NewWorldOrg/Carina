namespace Carina.Domain.Reservations;

public enum BroadcastGroupMove
{
    Regroup = 1,

    Retarget = 2,

    StepAside = 3,
}

public sealed record BroadcastGroupPlacing(BroadcastGroupMove Move, BroadcastGroupKey? Key, BroadcastGroupRole Role)
{
    public static BroadcastGroupPlacing Retargeting { get; } = new(BroadcastGroupMove.Retarget, null, BroadcastGroupRole.Standalone);

    public static BroadcastGroupPlacing SteppingAside { get; } = new(BroadcastGroupMove.StepAside, null, BroadcastGroupRole.Standalone);

    public static BroadcastGroupPlacing Regrouping(BroadcastGroupKey? key, BroadcastGroupRole role)
        => new(BroadcastGroupMove.Regroup, key, role);
}
