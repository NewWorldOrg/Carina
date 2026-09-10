using Carina.Domain.Library;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

/// <summary>
/// The escape is what keeps a title's own <c>%</c> or <c>_</c> from being read as a wildcard.
/// </summary>
internal static class SearchableText
{
    public static IQueryable<T> Carrying<T>(IQueryable<T> found, IReadOnlyList<string> words)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(words);

        return words.Aggregate(found, (carried, word) => Containing(carried, word));
    }

    private static IQueryable<T> Containing<T>(IQueryable<T> found, string word)
        where T : class
    {
        string pattern = RecordingSearchPattern.Containing(word);

        return found.Where(row => EF.Functions.ILike(
            EF.Property<string>(row, ProgrammeConfiguration.Searchable),
            pattern,
            RecordingSearchPattern.Escape));
    }
}
