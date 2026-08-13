using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence;

public class CarinaDbContext(DbContextOptions<CarinaDbContext> options) : DbContext(options);
