using WelcomeMat.Config;

namespace WelcomeMat.Data;

/// <summary>
/// An class representing the one place the SQLite connection string is built.
/// </summary>
public static class SqliteConfiguration
{
    /// <summary>
    /// Build the connection string; shared cache, so the pooled connections see
    /// each other's writes.
    /// </summary>
    /// <param name="options"></param>
    /// <returns></returns>
    public static string ConnectionString(AppOptions options) =>
        $"Data Source={options.DatabasePath};Mode=ReadWriteCreate;Cache=Shared;Foreign Keys=True;Default Timeout=5";
}
