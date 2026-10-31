using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ArteInventariado.Infrastructure.Data;

public class ArteInventariadoDbContextFactory
    : IDesignTimeDbContextFactory<ArteInventariadoDbContext>
{
    public ArteInventariadoDbContext CreateDbContext(string[] args)
    {
        var databasePath = DatabasePathProvider.GetDatabasePath();

        var optionsBuilder = new DbContextOptionsBuilder<ArteInventariadoDbContext>();
        optionsBuilder.UseSqlite($"Data Source={databasePath}");

        return new ArteInventariadoDbContext(optionsBuilder.Options);
    }
}
