namespace Carina.Domain.Encodings;

public enum EncodeUnaskedStanding
{
    Settled = 1,

    NothingIsDefined = 2,

    MoreThanOneIsOffered = 3,

    TheProfileIsNotOffered = 4,
}

/// <summary>
/// Where an artefact goes and what shape it takes when nobody said. A machine with one destination
/// still offered has no choice to make, and that destination already names the profile it encodes
/// with unless another is asked for; a machine with several has a choice nothing here is entitled
/// to make for a person, so it makes none and says why. The standing is what a caller shows or
/// logs, so a run that queues nothing is never silent about what is missing (BR-ED2-004).
/// </summary>
public sealed record EncodeUnasked
{
    private EncodeUnasked(EncodeDestination? destination, EncodeProfile? profile, EncodeUnaskedStanding standing)
    {
        Destination = destination;
        Profile = profile;
        Standing = standing;
    }

    public EncodeDestination? Destination { get; }

    public EncodeProfile? Profile { get; }

    public EncodeUnaskedStanding Standing { get; }

    public bool IsSettled => Standing is EncodeUnaskedStanding.Settled;

    public static EncodeUnasked Of(
        IReadOnlyList<EncodeDestination> destinations,
        IReadOnlyList<EncodeProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(profiles);

        EncodeDestination[] offered = [.. destinations.Where(destination => !destination.IsRetired)];

        if (offered is not [EncodeDestination only])
        {
            return new EncodeUnasked(
                null,
                null,
                offered.Length is 0 ? EncodeUnaskedStanding.NothingIsDefined : EncodeUnaskedStanding.MoreThanOneIsOffered);
        }

        EncodeProfile? named = profiles
            .FirstOrDefault(profile => profile.Id.Equals(only.DefaultProfileId) && !profile.IsRetired);

        return named is null
            ? new EncodeUnasked(null, null, EncodeUnaskedStanding.TheProfileIsNotOffered)
            : new EncodeUnasked(only, named, EncodeUnaskedStanding.Settled);
    }
}
