using System.Diagnostics.CodeAnalysis;

using Carina.Broadcast.Text;

namespace Carina.Broadcast.Descriptors;

public abstract record LogoTransmission
{
    public const int WithDownloadDataIdLength = 7;

    public const int SimpleLength = 3;

    private const int WithDownloadDataId = 0x01;

    private const int Simple = 0x02;

    private const int CharacterStringOnly = 0x03;

    private LogoTransmission()
    {
    }

    public sealed record InTheCommonDataTable : LogoTransmission
    {
        internal InTheCommonDataTable(int logoId, int? logoVersion, int? downloadDataId)
        {
            LogoId = logoId;
            LogoVersion = logoVersion;
            DownloadDataId = downloadDataId;
        }

        public int LogoId { get; }

        public int? LogoVersion { get; }

        public int? DownloadDataId { get; }
    }

    public sealed record ACharacterStringInstead : LogoTransmission
    {
        internal ACharacterStringInstead(string text)
        {
            Text = text;
        }

        public string Text { get; }
    }

    public static bool TryRead(Descriptor descriptor, [NotNullWhen(true)] out LogoTransmission? transmission)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        transmission = null;

        if (descriptor.Tag != DescriptorTags.LogoTransmission || descriptor.Payload.Length < 1)
        {
            return false;
        }

        ReadOnlySpan<byte> payload = descriptor.Payload.Span;

        switch (payload[0])
        {
            case WithDownloadDataId when payload.Length == WithDownloadDataIdLength:
                transmission = new InTheCommonDataTable(
                    LogoIdAt(payload, 1),
                    ((payload[3] & 0x0F) << 8) | payload[4],
                    (payload[5] << 8) | payload[6]);

                return true;

            case Simple when payload.Length == SimpleLength:
                transmission = new InTheCommonDataTable(LogoIdAt(payload, 1), null, null);

                return true;

            case CharacterStringOnly:
                transmission = new ACharacterStringInstead(AribText.Decode(payload[1..]));

                return true;

            default:
                return false;
        }
    }

    private static int LogoIdAt(ReadOnlySpan<byte> payload, int at)
        => ((payload[at] & 0x01) << 8) | payload[at + 1];
}
