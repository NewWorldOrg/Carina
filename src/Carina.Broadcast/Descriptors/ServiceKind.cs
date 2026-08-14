namespace Carina.Broadcast.Descriptors;

public enum ServiceKind
{
    Unknown = 0,

    Television = 0x01,

    Audio = 0x02,

    TemporaryVideo = 0xA1,

    TemporaryAudio = 0xA2,

    TemporaryData = 0xA3,

    Engineering = 0xA4,

    PromotionVideo = 0xA5,

    PromotionAudio = 0xA6,

    PromotionData = 0xA7,

    PreStoredData = 0xA8,

    StoreOnlyData = 0xA9,

    BookmarkList = 0xAA,

    ServerSimultaneous = 0xAB,

    IndependentFile = 0xAC,

    UltraHighDefinitionTelevision = 0xAD,

    Data = 0xC0,

    StoredUsingTlv = 0xC1,

    Multimedia = 0xC2,
}

public static class ServiceKinds
{
    public static ServiceKind Of(byte serviceType)
        => Enum.IsDefined((ServiceKind)serviceType) ? (ServiceKind)serviceType : ServiceKind.Unknown;
}
