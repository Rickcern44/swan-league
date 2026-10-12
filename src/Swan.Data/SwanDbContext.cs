using Microsoft.EntityFrameworkCore;

namespace Swan.Data;

public sealed class SwanDbContext(DbContextOptions<SwanDbContext> options) : DbContext(options);
