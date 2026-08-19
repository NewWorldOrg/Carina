using Carina.Domain.Auth;

namespace Carina.Api.Authentication;

public static class DeviceLabel
{
    public const string Unnamed = "An unnamed device";

    public static string From(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return Unnamed;
        }

        string plain = new string([.. userAgent.Where(letter => !char.IsControl(letter))]).Trim();

        if (plain.Length > AuthSession.LongestDeviceLabel)
        {
            plain = plain[..AuthSession.LongestDeviceLabel].Trim();
        }

        return plain.Length == 0 ? Unnamed : plain;
    }
}
