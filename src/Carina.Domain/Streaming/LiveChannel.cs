namespace Carina.Domain.Streaming;

public enum LiveChannel : byte
{
    PictureHeader = 0x00,

    Picture = 0x01,

    SoundHeader = 0x10,

    Sound = 0x11,

    CaptionHeader = 0x20,

    Caption = 0x21,

    ServiceInformation = 0x30,

    Control = 0x40,
}
