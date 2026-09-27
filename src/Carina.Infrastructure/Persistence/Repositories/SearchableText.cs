using Carina.Domain.Library;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

/// <summary>
/// Narrows rows to those whose searchable text contains every one of the words, with one
/// <c>ILIKE</c> per word. Each word is made a pattern by <see cref="RecordingSearchPattern.Containing"/>,
/// which escapes its <c>%</c> and <c>_</c>.
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
