using Carina.Broadcast.Images;

namespace Carina.Broadcast.Tables;

public sealed class CarriedLogo
{
    internal CarriedLogo(int logoType, int logoId, int logoVersion, ReadOnlyMemory<byte> asBroadcast)
    {
        LogoType = logoType;
        LogoId = logoId;
        LogoVersion = logoVersion;
        AsBroadcast = asBroadcast;

        Image = AribLogoImage.TryRead(asBroadcast, out AribLogoImage? read) ? read : null;
    }

    public int LogoType { get; }

    public int LogoId { get; }

    public int LogoVersion { get; }

    public ReadOnlyMemory<byte> AsBroadcast { get; }

    public AribLogoImage? Image { get; }

    public bool IsAPicture => Image is not null;
}
