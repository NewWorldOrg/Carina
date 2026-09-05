namespace Carina.Domain.Encodings;

/// <summary>
/// Which encoder a run asked for and which it ran on, kept with the job so that a run that was
/// degraded says so in the ledger and not only in a log (BR-EV-004). A swerve is present exactly
/// when the two differ.
/// </summary>
public sealed record EncodeRoute
{
    public EncodeRoute(EncodeEncoder asked, EncodeEncoder ran, EncodeSwerve? swerved)
    {
        Asked = EncodeShapes.Named(asked);
        Ran = EncodeShapes.Named(ran);

        if (swerved is { } because && !Enum.IsDefined(because))
        {
            throw new ArgumentOutOfRangeException(nameof(swerved), because, "A run swerves for one of the reasons named.");
        }

        if ((Asked == Ran) != (swerved is null))
        {
            throw new ArgumentException(
                "A run that ran where it was sent did not swerve, and one that ran elsewhere says why.",
                nameof(swerved));
        }

        Swerved = swerved;
    }

    public EncodeEncoder Asked { get; }

    public EncodeEncoder Ran { get; }

    public EncodeSwerve? Swerved { get; }

    public bool WasDegraded => Swerved is not null;

    public static EncodeRoute Of(EncodeEncoder asked, EncodePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Encoder is { } ran
            ? new EncodeRoute(asked, ran, plan.Swerved)
            : throw new ArgumentException("A plan that runs nowhere is no route.", nameof(plan));
    }
}
