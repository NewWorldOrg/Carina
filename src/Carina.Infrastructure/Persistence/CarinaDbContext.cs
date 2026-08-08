using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence;

/// <summary>
/// Single persistence context of the app process.
/// </summary>
/// <remarks>
/// Entity configuration is added per domain. Aggregates stay decoupled on purpose:
/// reservations persist by their broadcast identifiers instead of holding a foreign
/// key to the channel definitions, and the programme cache is disposable — dropping
/// it must never take reservations with it.
/// </remarks>
public class CarinaDbContext(DbContextOptions<CarinaDbContext> options) : DbContext(options);
