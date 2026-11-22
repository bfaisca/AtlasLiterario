using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AtlasLiterario.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public const string ApplyMigrationsSetting = "Database:ApplyMigrationsOnStartup";

    /// <summary>Applies pending EF Core migrations when <c>Database:ApplyMigrationsOnStartup=true</c>.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue<bool>(ApplyMigrationsSetting))
        {
            return;
        }

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AtlasLiterario.DatabaseInitializer");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!db.Database.IsRelational())
        {
            await db.Database.EnsureCreatedAsync(ct);
            return;
        }

        if (!db.Database.GetMigrations().Any())
        {
            logger.LogWarning("No EF Core migrations found. Generate them with scripts/add-migration.sh.");
            return;
        }

        logger.LogInformation("Applying database migrations...");
        await db.Database.MigrateAsync(ct);
    }
}
