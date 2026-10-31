namespace ArteInventariado.Infrastructure.Data;

public static class DatabaseSeeder
{
    public static Task SeedAsync(AppDbContext context)
    {
        return DatabaseInitializer.InicializarAsync(context);
    }
}
