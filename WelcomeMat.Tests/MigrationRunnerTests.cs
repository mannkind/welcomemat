using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace WelcomeMat.Tests;

public class MigrationRunnerTests
{
    private SqliteTestDatabase Db = null!;

    [SetUp]
    public void SetUp() => this.Db = new SqliteTestDatabase();

    [TearDown]
    public void TearDown() => this.Db.Dispose();

    [Test]
    public void FailedMigration_RollsBackStatementsAndMarker()
    {
        this.Db.WriteMigration("001_test.sql",
            "CREATE TABLE first(id INTEGER); INVALID SQL; CREATE TABLE last(id INTEGER);");

        Assert.ThrowsAsync<SqliteException>(() => this.Db.Runner().RunAsync());

        Assert.Multiple(async () =>
        {
            Assert.That(await this.Db.TableExists("first"), Is.False);
            Assert.That(await this.Db.TableExists("last"), Is.False);
            Assert.That(await this.Db.MigrationMarked("001_test.sql"), Is.False);
        });
    }

    [Test]
    public async Task SuccessfulMigration_CommitsAllStatementsAndMarker()
    {
        this.Db.WriteMigration("001_test.sql",
            "CREATE TABLE first(id INTEGER); CREATE TABLE second(id INTEGER);");

        await this.Db.Runner().RunAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await this.Db.TableExists("first"), Is.True);
            Assert.That(await this.Db.TableExists("second"), Is.True);
            Assert.That(await this.Db.MigrationMarked("001_test.sql"), Is.True);
        });
    }

    [Test]
    public async Task AlreadyAppliedMigration_IsNotReapplied()
    {
        this.Db.WriteMigration("001_test.sql", "CREATE TABLE first(id INTEGER);");

        await this.Db.Runner().RunAsync();
        await this.Db.Runner().RunAsync();

        Assert.That(await this.Db.TableExists("first"), Is.True);
    }
}
