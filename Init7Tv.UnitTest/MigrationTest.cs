using Init7Tv.Dal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Init7Tv.UnitTest;

/// <summary>
/// Migrations run automatically on startup, so a broken one takes the app down
/// on boot rather than at build time.
/// </summary>
public class MigrationTest
{
    [Test]
    public void AllMigrations_ApplyToAFreshDatabase()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<Init7TvContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new Init7TvContext(options);

        Assert.DoesNotThrow(() => context.Database.Migrate());
        Assert.That(context.Database.GetPendingMigrations(), Is.Empty);
    }
}
