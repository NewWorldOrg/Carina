using Carina.Db;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

if (args is not ["--migrate"])
{
    Console.Error.WriteLine("usage: Carina.Db --migrate");
    return 64;
}

CarinaDbContext context;
try
{
    context = new CarinaDbContextFactory().CreateDbContext(args);
}
catch (InvalidOperationException error)
{
    Console.Error.WriteLine(error.Message);
    return 78;
}

await using (context)
{
    await context.Database.MigrateAsync();
}

return 0;
