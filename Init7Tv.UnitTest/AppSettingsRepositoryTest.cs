using Init7Tv.Dal;
using Init7Tv.Dal.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Init7Tv.UnitTest;

public class AppSettingsRepositoryTest
{
    private SqliteConnection m_connection = null!;
    private Init7TvContext m_context = null!;
    private AppSettingsRepository m_repository = null!;

    [SetUp]
    public void SetUp()
    {
        // in-memory sqlite keeps the real provider behaviour, unlike the InMemory provider
        m_connection = new SqliteConnection("DataSource=:memory:");
        m_connection.Open();

        var options = new DbContextOptionsBuilder<Init7TvContext>()
            .UseSqlite(m_connection)
            .Options;

        m_context = new Init7TvContext(options);
        m_context.Database.EnsureCreated();

        m_repository = new AppSettingsRepository(m_context);
    }

    [TearDown]
    public void TearDown()
    {
        m_context.Dispose();
        m_connection.Dispose();
    }

    [Test]
    public async Task CreateAppSettings_AddsTheRowOnAnEmptyDatabase()
    {
        await m_repository.CreateAppSettingsAsync();

        Assert.That(await m_context.AppSettings.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task CreateAppSettings_DoesNotAddAnotherRowOnEveryStart()
    {
        await m_repository.CreateAppSettingsAsync();
        await m_repository.CreateAppSettingsAsync();
        await m_repository.CreateAppSettingsAsync();

        Assert.That(await m_context.AppSettings.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task CreateAppSettings_KeepsTheConfiguredRowWhenPruningOldDuplicates()
    {
        await m_repository.CreateAppSettingsAsync();
        await m_repository.UpdateGeneralSettingsAsync(new Dal.Entities.AppSettings { BaseDomain = "https://tv.example.org" });

        // rows an earlier version appended on each restart
        m_context.AppSettings.Add(new Dal.Entities.AppSettings());
        m_context.AppSettings.Add(new Dal.Entities.AppSettings());
        await m_context.SaveChangesAsync();

        await m_repository.CreateAppSettingsAsync();

        var remaining = await m_context.AppSettings.SingleAsync();
        Assert.That(remaining.BaseDomain, Is.EqualTo("https://tv.example.org"));
    }

    [Test]
    public async Task GetAppSettings_ReportsNotFoundBeforeSeeding()
    {
        var result = await m_repository.GetAppSettingsAsync();

        Assert.That(result.ResultCode, Is.EqualTo(Shared.ResultCode.NotFound));
    }

    [Test]
    public async Task UpdateGeneralSettings_LeavesTheEmailSettingsAlone()
    {
        await m_repository.CreateAppSettingsAsync();
        await m_repository.UpdateEmailAppSettingsAsync(new Dal.Entities.AppSettings { SmtpHost = "smtp.example.org", Port = 587 });

        await m_repository.UpdateGeneralSettingsAsync(new Dal.Entities.AppSettings { BaseDomain = "https://tv.example.org" });

        var settings = (await m_repository.GetAppSettingsAsync()).Value;
        Assert.Multiple(() =>
        {
            Assert.That(settings.SmtpHost, Is.EqualTo("smtp.example.org"));
            Assert.That(settings.Port, Is.EqualTo(587));
            Assert.That(settings.BaseDomain, Is.EqualTo("https://tv.example.org"));
        });
    }
}
