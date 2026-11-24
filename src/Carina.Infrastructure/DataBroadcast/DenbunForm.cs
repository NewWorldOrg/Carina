using System.Text;

namespace Carina.Infrastructure.DataBroadcast;

public static class DenbunForm
{
    public const string MediaType = "application/x-www-form-urlencoded";

    private const string Name = "Denbun=";

    public static byte[] Of(ReadOnlySpan<byte> text)
    {
        StringBuilder form = new(Name, Name.Length + (text.Length * 3));

        foreach (byte value in text)
        {
            Write(form, value);
        }

        return Encoding.ASCII.GetBytes(form.ToString());
    }

    private static void Write(StringBuilder form, byte value)
    {
        if (IsWrittenAsItIs(value))
        {
            form.Append((char)value);

            return;
        }

        if (value == (byte)' ')
        {
            form.Append('+');

            return;
        }

        form.Append('%').Append(Convert.ToHexString([value]));
    }

    private static bool IsWrittenAsItIs(byte value)
        => char.IsAsciiLetterOrDigit((char)value) || value is (byte)'-' or (byte)'_' or (byte)'.';
}
