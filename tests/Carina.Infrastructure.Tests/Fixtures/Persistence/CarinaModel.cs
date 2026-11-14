using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Carina.Infrastructure.Tests.Fixtures.Persistence;

internal static class CarinaModel
{
    private const string Placeholder = "Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder";

    public static DbContextOptions<CarinaDbContext> Options()
    {
        var builder = new DbContextOptionsBuilder<CarinaDbContext>();
        builder.UseCarinaDatabase(Placeholder);

        return builder.Options;
    }

    public static CarinaDbContext Context() => new(Options());

    public static IModel Schema(CarinaDbContext context)
        => context.GetService<IDesignTimeModel>().Model;

    public static IEntityType Entity<TEntity>(CarinaDbContext context)
        => Schema(context).FindEntityType(typeof(TEntity))!;

    // Tuning and the signal readings are complex properties rather than owned entities:
    // they carry no identity, so two rows may hold the very same instance.
    public static IEnumerable<string> ColumnsOf(IEntityType entity, string valueObject)
        => entity.GetComplexProperties()
            .Single(property => property.Name == valueObject)
            .ComplexType.GetProperties()
            .Select(property => property.GetColumnName());

    public static IEnumerable<IProperty> EveryProperty(ITypeBase type)
        => [
            .. type.GetProperties(),
            .. type.GetComplexProperties().SelectMany(property => EveryProperty(property.ComplexType)),
        ];
}
