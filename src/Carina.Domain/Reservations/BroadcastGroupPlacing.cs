namespace Carina.Domain.Reservations;

/// <summary>
/// What becomes of a reservation the broadcast groups place: it keeps its listing and takes the key and role given, it
/// moves onto the primary listing, or it steps aside for the reservation already standing there.
/// </summary>
public enum BroadcastGroupMove
{
    Regroup = 1,

    Retarget = 2,

    StepAside = 3,
}

/// <summary>
/// The move a reservation the broadcast groups place makes, with the key and role it takes when it is regrouped.
/// </summary>
public sealed record BroadcastGroupPlacing(BroadcastGroupMove Move, BroadcastGroupKey? Key, BroadcastGroupRole Role)
{
    public static BroadcastGroupPlacing Retargeting { get; } = new(BroadcastGroupMove.Retarget, null, BroadcastGroupRole.Standalone);

    public static BroadcastGroupPlacing SteppingAside { get; } = new(BroadcastGroupMove.StepAside, null, BroadcastGroupRole.Standalone);

    public static BroadcastGroupPlacing Regrouping(BroadcastGroupKey? key, BroadcastGroupRole role)
        => new(BroadcastGroupMove.Regroup, key, role);
}
