namespace Carina.Domain.Encodings;

public enum EncodeCodec
{
    H264 = 1,

    H265 = 2,
}

public enum EncodeResolution
{
    AsSource = 1,

    FullHd = 2,

    Hd = 3,
}

public enum Deinterlace
{
    Leave = 1,

    EveryFrame = 2,

    EveryField = 3,
}

public enum EncodeEncoder
{
    Software = 1,

    Vaapi = 2,
}
