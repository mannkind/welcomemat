using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using DbUp;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace WelcomeMat.Data;

/// <summary>
/// An class representing a managed way to run the SQL migrations.
/// </summary>
public class MigrationRunner
{
    /// <summary>
    /// Initializes a new instance of the MigrationRunner class.
    /// </summary>
    /// <param name="connectionString"></param>
    /// <param name="migrationDirectory"></param>
    /// <param name="logger"></param>
    public MigrationRunner(string connectionString, string migrationDirectory, ILogger<MigrationRunner> logger)
    {
        this.ConnectionString = connectionString;
        this.MigrationDirectory = migrationDirectory;
        this.Logger = logger;
    }

    /// <summary>
    /// Run the pending migrations and put the database in WAL mode.
    /// </summary>
    /// <param name="ct"></param>
    public async Task RunAsync(CancellationToken ct = default)
    {
        await this.EnableWalAsync(ct);

        var result = DeployChanges.To
            .SqliteDatabase(this.ConnectionString)
            .WithScriptsFromFileSystem(this.MigrationDirectory)
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build()
            .PerformUpgrade();

        foreach (var script in result.Scripts)
        {
            this.Logger.LogInformation("Applied migration {Name}", script.Name);
        }

        if (!result.Successful)
        {
            ExceptionDispatchInfo.Capture(result.Error).Throw();
        }
    }

    /// <summary>
    /// Switch to WAL, so a reader does not block the writer.
    /// </summary>
    /// <param name="ct"></param>
    private async Task EnableWalAsync(CancellationToken ct)
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// The connection string used internally.
    /// </summary>
    private readonly string ConnectionString;

    /// <summary>
    /// The directory holding the migration scripts.
    /// </summary>
    private readonly string MigrationDirectory;

    /// <summary>
    /// The logger used internally.
    /// </summary>
    private readonly ILogger<MigrationRunner> Logger;
}
