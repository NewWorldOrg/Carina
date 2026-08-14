namespace Carina.Contracts;

public static class SessionPurposes
{
    public static bool ReadsEveryPacket(SessionPurpose purpose) =>
        purpose is SessionPurpose.Survey or SessionPurpose.Scan;
}
