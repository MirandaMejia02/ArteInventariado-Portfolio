using Microsoft.EntityFrameworkCore;

namespace ArteInventariado.Infrastructure.Data;

// Compatibility wrapper for the previous prototype context and its old migration files.
public class ArteInventariadoDbContext : AppDbContext
{
    public ArteInventariadoDbContext(DbContextOptions<ArteInventariadoDbContext> options)
        : base(options)
    {
    }
}
