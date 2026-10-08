using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WelcomeMat.Config;
using WelcomeMat.Services;

namespace WelcomeMat.Data;

/// <summary>
/// An class representing the startup work: migrate, seed, and tidy up.
/// </summary>
public class MigrationHostedService : IHostedService
{
    /// <summary>
    /// Initializes a new instance of the MigrationHostedService class.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="logger"></param>
    /// <param name="environment"></param>
    /// <param name="services"></param>
    /// <param name="images"></param>
    public MigrationHostedService(IOptions<AppOptions> options, ILogger<MigrationRunner> logger, IHostEnvironment environment, IServiceProvider services, PropertyImageService images)
    {
        this.Options = options;
        this.Logger = logger;
        this.Environment = environment;
        this.Services = services;
        this.Images = images;
    }

    private const int MAX_SESSION_CLEANUP_ROWS = 1000;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connStr = SqliteConfiguration.ConnectionString(this.Options.Value);
        var migrationDir = Path.Combine(AppContext.BaseDirectory, "Data", "Migrations");
        var runner = new MigrationRunner(connStr, migrationDir, this.Logger);
        await runner.RunAsync(cancellationToken);

        await using (var cleanupScope = this.Services.CreateAsyncScope())
        {
            var validator = cleanupScope.ServiceProvider.GetRequiredService<SessionValidationService>();
            var removed = await validator.CleanupInvalidAsync(MAX_SESSION_CLEANUP_ROWS, cancellationToken);
            if (removed > 0)
            {
                this.Logger.LogInformation("Removed {Count} invalid/expired session(s) at startup.", removed);
            }
        }

        await using (var imageCleanupScope = this.Services.CreateAsyncScope())
        {
            var db = imageCleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var removedDirs = await this.Images.CleanupOrphansAsync(db, cancellationToken);
            if (removedDirs > 0)
            {
                this.Logger.LogInformation("Removed {Count} orphaned property image director(y/ies) at startup.", removedDirs);
            }
        }

        if (this.Options.Value.TestAuthEnabled)
        {
            await using var scope = this.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!db.Users.Any())
            {
                await DevSeedService.SeedAsync(db);
                this.Logger.LogInformation("Dev seed applied.");
            }
            return;
        }

        await using var bootstrapScope = this.Services.CreateAsyncScope();
        var bootstrap = bootstrapScope.ServiceProvider.GetRequiredService<BootstrapAdminService>();
        await bootstrap.BootstrapAsync(
            this.Options.Value.InitialUser,
            requireInitialAdministrator: this.Environment.IsProduction(),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// The application options.
    /// </summary>
    private readonly IOptions<AppOptions> Options;

    /// <summary>
    /// The logger used internally.
    /// </summary>
    private readonly ILogger<MigrationRunner> Logger;

    /// <summary>
    /// The environment the application is running in.
    /// </summary>
    private readonly IHostEnvironment Environment;

    /// <summary>
    /// The provider used to resolve a scoped database.
    /// </summary>
    private readonly IServiceProvider Services;

    /// <summary>
    /// The service used to store and serve property photos.
    /// </summary>
    private readonly PropertyImageService Images;
}
