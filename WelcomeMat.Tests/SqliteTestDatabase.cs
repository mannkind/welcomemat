using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using WelcomeMat.Data;

namespace WelcomeMat.Tests;

public class SqliteTestDatabase : IDisposable
{
    private readonly string RootDir;
    private readonly string ScratchMigrationDir;

    public string ConnectionString { get; }
    public string ProductionMigrationsDir { get; }

    public SqliteTestDatabase()
    {
        this.RootDir = Directory.CreateTempSubdirectory("welcomemat-migrations-").FullName;
        var dbPath = Path.Combine(this.RootDir, "test.db");
        this.ConnectionString =
            $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;Foreign Keys=True;Default Timeout=5";

        this.ScratchMigrationDir = Path.Combine(this.RootDir, "scratch-migrations");
        Directory.CreateDirectory(this.ScratchMigrationDir);

        this.ProductionMigrationsDir = Path.Combine(AppContext.BaseDirectory, "Data", "Migrations");
    }

    public void WriteMigration(string name, string sql) =>
        File.WriteAllText(Path.Combine(this.ScratchMigrationDir, name), sql);

    public MigrationRunner Runner() =>
        new(this.ConnectionString, this.ScratchMigrationDir, NullLogger<MigrationRunner>.Instance);

    public MigrationRunner RunnerWithAllMigrations() =>
        new(this.ConnectionString, this.ProductionMigrationsDir, NullLogger<MigrationRunner>.Instance);

    public async Task<bool> TableExists(string name)
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    public async Task<bool> IndexExists(string name)
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    public async Task<bool> MigrationMarked(string name)
    {
        if (!await this.TableExists("SchemaVersions"))
        {
            return false;
        }

        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = @name";
        cmd.Parameters.AddWithValue("@name", name);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    public async Task<long> Count(string table)
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<string?> ForeignKeyDeleteAction(string table, string referencedTable)
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_key_list({table})";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var referenced = reader.GetString(reader.GetOrdinal("table"));
            if (referenced.Equals(referencedTable, StringComparison.OrdinalIgnoreCase))
            {
                return reader.GetString(reader.GetOrdinal("on_delete"));
            }
        }
        return null;
    }

    public async Task<string> JournalMode()
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<bool> ForeignKeysEnabled()
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys";
        return (long)(await cmd.ExecuteScalarAsync())! == 1;
    }

    public async Task<long> BusyTimeoutMs()
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        return conn.DefaultTimeout * 1000L;
    }

    private static async Task ExecAsync(
        SqliteConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (paramName, value) in parameters)
        {
            cmd.Parameters.AddWithValue(paramName, value);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(this.RootDir, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a test run over a temp directory.
        }
    }
}
