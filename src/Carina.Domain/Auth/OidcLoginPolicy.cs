namespace Carina.Domain.Auth;

public sealed record OidcLoginPolicy
{
    public OidcLoginPolicy(TimeSpan handshakeLifetime, TimeSpan clockSkew, TimeSpan directoryLifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(handshakeLifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(clockSkew, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(directoryLifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(clockSkew, handshakeLifetime);

        HandshakeLifetime = handshakeLifetime;
        ClockSkew = clockSkew;
        DirectoryLifetime = directoryLifetime;
    }

    public static OidcLoginPolicy Default { get; } =
        new(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(15));

    public TimeSpan HandshakeLifetime { get; }

    public TimeSpan ClockSkew { get; }

    public TimeSpan DirectoryLifetime { get; }
}
