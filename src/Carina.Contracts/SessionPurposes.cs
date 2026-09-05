namespace Carina.Contracts;

public static class SessionPurposes
{
    public static readonly IReadOnlyList<SessionPurpose> Baseline =
    [
        SessionPurpose.Recording,
        SessionPurpose.Live,
        SessionPurpose.Survey,
        SessionPurpose.Scan,
    ];

    public static IReadOnlyList<string> Capabilities =>
        [.. Enum.GetValues<SessionPurpose>().Select(Capability).OfType<string>()];

    public static bool ReadsEveryPacket(SessionPurpose purpose) =>
        purpose is SessionPurpose.Survey or SessionPurpose.SurveyNow or SessionPurpose.Scan
            or SessionPurpose.Logo;

    public static string? Capability(SessionPurpose purpose) =>
        purpose is SessionPurpose.Unspecified || Baseline.Contains(purpose)
            ? null
            : DriverCapabilities.Purpose(SessionPurposeConverter.WireName(purpose));

    public static SessionPurpose Degrades(SessionPurpose purpose) =>
        purpose switch
        {
            SessionPurpose.SurveyNow => SessionPurpose.Survey,
            _ => SessionPurpose.Unspecified,
        };

    public static SessionPurpose AgreedWith(DriverHello hello, SessionPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(hello);

        SessionPurpose candidate = purpose;

        while (candidate is not SessionPurpose.Unspecified)
        {
            if (Capability(candidate) is not { } capability || hello.Supports(capability))
            {
                return candidate;
            }

            candidate = Degrades(candidate);
        }

        return SessionPurpose.Unspecified;
    }
}
