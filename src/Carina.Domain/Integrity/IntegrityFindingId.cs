using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public sealed class IntegrityFindingId : CommonValueObject<Guid>
{
    private const string WhatTheseNamesAreAbout = "carina:integrity-finding";

    public IntegrityFindingId(Guid value)
        : base(Validated(value))
    {
    }

    public static IntegrityFindingId Of(
        IntegrityFault fault,
        OutputRoot root,
        string path,
        RecordingId? recordingId)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(path);

        string fact = string.Join(
            '\n',
            WhatTheseNamesAreAbout,
            IntegrityFaults.Named(fault).ToString(),
            root.Value,
            path,
            recordingId?.Wire ?? string.Empty);

        return new IntegrityFindingId(Folded(SHA256.HashData(Encoding.UTF8.GetBytes(fact))));
    }

    private static Guid Folded(byte[] digest)
    {
        Span<byte> taken = digest.AsSpan(0, 16);

        taken[6] = (byte)((taken[6] & 0x0F) | 0x50);
        taken[8] = (byte)((taken[8] & 0x3F) | 0x80);

        return new Guid(taken, bigEndian: true);
    }

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A finding id cannot be empty.", nameof(value));
        }

        return value;
    }
}
