using Carina.Db;

using Microsoft.EntityFrameworkCore;

if (args is not ["--migrate"])
{
    Console.Error.WriteLine("usage: Carina.Db --migrate");
    return 64;
}

using var context = new CarinaDbContextFactory().CreateDbContext(args);
await context.Database.MigrateAsync();

return 0;
