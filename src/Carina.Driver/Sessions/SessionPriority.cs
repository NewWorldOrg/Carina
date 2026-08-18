using Carina.Contracts;

namespace Carina.Driver.Sessions;

public static class SessionPriority
{
    public const int Recording = 10;

    public const int Live = 9;

    public const int GuideNow = 8;

    public const int Scan = 5;

    public const int Guide = 3;

    public const int LogoCapture = 1;

    public const int Unknown = 0;

    public static int Of(SessionPurpose purpose) =>
        purpose switch
        {
            SessionPurpose.Recording => Recording,
            SessionPurpose.Live => Live,
            SessionPurpose.Scan => Scan,
            SessionPurpose.Survey => Guide,
            SessionPurpose.SurveyNow => GuideNow,
            _ => Unknown,
        };
}
