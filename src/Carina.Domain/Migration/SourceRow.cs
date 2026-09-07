namespace Carina.Domain.Migration;

public static class SourceRow
{
    public static long Of(long id, string parameterName)
        => id > 0
            ? id
            : throw new ArgumentOutOfRangeException(parameterName, id, "A row of the source system is numbered from one.");
}
