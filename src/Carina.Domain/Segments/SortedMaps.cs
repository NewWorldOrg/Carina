using System.Collections.ObjectModel;

namespace Carina.Domain.Segments;

/// <summary>
/// Copies a map into one that nothing can change and that lists its keys in order, and compares two
/// such maps by what they hold.
/// </summary>
internal static class SortedMaps
{
    public static ReadOnlyDictionary<TKey, TValue> Copied<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> map, IComparer<TKey> order)
        where TKey : notnull
        => new(new SortedDictionary<TKey, TValue>(map.ToDictionary(), order));

    public static bool Same<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> one, IReadOnlyDictionary<TKey, TValue> other)
        where TKey : notnull
        => one.Count == other.Count
           && one.All(pair => other.TryGetValue(pair.Key, out TValue? found) && EqualityComparer<TValue>.Default.Equals(pair.Value, found));

    public static int Hash<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> map)
        where TKey : notnull
    {
        HashCode hash = default;

        foreach ((TKey key, TValue value) in map)
        {
            hash.Add(key);
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
