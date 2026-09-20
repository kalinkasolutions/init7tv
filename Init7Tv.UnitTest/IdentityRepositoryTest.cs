using Init7Tv.Dal;
using Init7Tv.Dal.Repositories;
using Init7Tv.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Init7Tv.UnitTest;

/// <summary>
/// Against the real Identity stack rather than a mocked one: what is being checked is the rules
/// layered on top of it — who may lose a role and who may not — and a mock of UserManager would be
/// checking the mock.
/// </summary>
public class IdentityRepositoryTest
{
    private const string Password = "Hunter2!x";

    private SqliteConnection m_connection = null!;
    private ServiceProvider m_services = null!;
    private IServiceScope m_scope = null!;
    private UserManager<IdentityUser> m_users = null!;
    private RoleManager<IdentityRole> m_roles = null!;
    private IdentityRepository m_repository = null!;

    [SetUp]
    public async Task SetUp()
    {
        m_connection = new SqliteConnection("DataSource=:memory:");
        m_connection.Open();

        m_services = new ServiceCollection()
            .AddLogging()
            // the reset token is a protected payload, so the providers need this behind them
            .AddDataProtection()
            .Services
            .AddDbContext<Init7TvContext>(options => options.UseSqlite(m_connection))
            .AddIdentityCore<IdentityUser>(options =>
            {
                options.Password.RequireUppercase = false;
                options.User.RequireUniqueEmail = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<Init7TvContext>()
            // the reset token a password change goes through comes from these
            .AddDefaultTokenProviders()
            .Services
            .BuildServiceProvider();

        m_scope = m_services.CreateScope();
        m_scope.ServiceProvider.GetRequiredService<Init7TvContext>().Database.EnsureCreated();

        m_users = m_scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        m_roles = m_scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        m_repository = new IdentityRepository(NullLogger<IdentityRepository>.Instance, m_users, m_roles);

        foreach (var role in Init7TvRoles.Roles)
        {
            await m_roles.CreateAsync(new IdentityRole(role));
        }
    }

    [TearDown]
    public void TearDown()
    {
        // owned by the scope, but the analyser wants to see it said
        m_users.Dispose();
        m_roles.Dispose();
        m_scope.Dispose();
        m_services.Dispose();
        m_connection.Dispose();
    }

    private async Task<IdentityUser> Add(string userName, params string[] roles)
    {
        var user = new IdentityUser { UserName = userName, Email = $"{userName}@example.org" };
        var added = await m_repository.AddUserAsync(user, roles, Password);

        Assert.That(added.IsSuccess, Is.True, added.ErrorMessage);

        return user;
    }

    private async Task<string[]> RolesOf(IdentityUser user) =>
        (await m_users.GetRolesAsync(user)).Order().ToArray();

    private static IdentityUser Changed(IdentityUser user, string? userName = null) => new()
    {
        Id = user.Id,
        UserName = userName ?? user.UserName,
        Email = user.Email
    };

    [Test]
    public async Task ANewUserGetsTheRolesTheyWereGiven()
    {
        var user = await Add("bob", Init7TvRoles.Recording);

        Assert.That(await RolesOf(user), Is.EqualTo(new[] { Init7TvRoles.Recording }));
    }

    [Test]
    public async Task TheLastAdminCannotBeDemoted()
    {
        // there would then be nobody who could put it back
        var admin = await Add("root", Init7TvRoles.Admin);

        var result = await m_repository.UpdateUserAsync(Changed(admin), [Init7TvRoles.Recording], null);

        Assert.That(result.ResultCode, Is.EqualTo(ResultCode.Conflict));
        Assert.That(await RolesOf(admin), Is.EqualTo(new[] { Init7TvRoles.Admin }), "and still is one");
    }

    [Test]
    public async Task TheLastAdminCannotBeDeleted()
    {
        var admin = await Add("root", Init7TvRoles.Admin);

        var result = await m_repository.DeleteUserAsync(admin.Id);

        Assert.That(result.ResultCode, Is.EqualTo(ResultCode.Conflict));
        Assert.That(await m_users.FindByIdAsync(admin.Id), Is.Not.Null);
    }

    [Test]
    public async Task OneOfTwoAdminsCanBeDemoted()
    {
        await Add("root", Init7TvRoles.Admin);
        var other = await Add("bob", Init7TvRoles.Admin);

        var result = await m_repository.UpdateUserAsync(Changed(other), [Init7TvRoles.Recording], null);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(await RolesOf(other), Is.EqualTo(new[] { Init7TvRoles.Recording }));
    }

    [Test]
    public async Task ARoleThatDoesNotExistIsRefusedRatherThanSkipped()
    {
        // skipping it would take the role away without ever saying so
        var user = await Add("bob", Init7TvRoles.Recording);

        var result = await m_repository.UpdateUserAsync(Changed(user), ["Wizard"], null);

        Assert.That(result.ResultCode, Is.EqualTo(ResultCode.Invalid));
        Assert.That(result.ErrorMessage, Does.Contain("Wizard"));
    }

    [Test]
    public async Task ARefusedUpdateLeavesTheRolesAsTheyWere()
    {
        // the old code removed them all first, so a refusal part way through
        // left the user with none
        var user = await Add("bob", Init7TvRoles.Recording);

        await m_repository.UpdateUserAsync(Changed(user), ["Wizard"], null);

        Assert.That(await RolesOf(user), Is.EqualTo(new[] { Init7TvRoles.Recording }));
    }

    [Test]
    public async Task ARefusedUpdateChangesNothingElseEither()
    {
        var user = await Add("bob", Init7TvRoles.Recording);

        await m_repository.UpdateUserAsync(Changed(user, userName: "robert"), ["Wizard"], null);

        Assert.That((await m_users.FindByIdAsync(user.Id))!.UserName, Is.EqualTo("bob"));
    }

    [Test]
    public async Task TheLastAdminIsSafeEvenFromARoleThatIsNotReal()
    {
        // naming a role that does not exist used to walk past the guard, have every
        // role removed, then skip the add: the last admin silently demoted
        await m_roles.DeleteAsync((await m_roles.FindByNameAsync(Init7TvRoles.Admin))!);
        var admin = await Add("root");
        await m_users.AddToRoleAsync(admin, Init7TvRoles.Recording);

        var result = await m_repository.UpdateUserAsync(Changed(admin), [Init7TvRoles.Admin], null);

        Assert.That(result.ResultCode, Is.EqualTo(ResultCode.Invalid));
        Assert.That(await RolesOf(admin), Is.EqualTo(new[] { Init7TvRoles.Recording }));
    }

    [Test]
    public async Task ARoleThatIsNotChangingIsLeftAlone()
    {
        await Add("root", Init7TvRoles.Admin);
        var user = await Add("bob", Init7TvRoles.Admin, Init7TvRoles.Recording);

        var result = await m_repository.UpdateUserAsync(
            Changed(user), [Init7TvRoles.Admin, Init7TvRoles.Recording], null);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(await RolesOf(user), Is.EqualTo(new[] { Init7TvRoles.Admin, Init7TvRoles.Recording }.Order()));
    }

    [Test]
    public async Task UpdatingChangesTheNameAndEmail()
    {
        await Add("root", Init7TvRoles.Admin);
        var user = await Add("bob");

        var result = await m_repository.UpdateUserAsync(
            new IdentityUser { Id = user.Id, UserName = "robert", Email = "robert@example.org" }, [], null);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.UserName, Is.EqualTo("robert"));
            Assert.That(result.Value.Email, Is.EqualTo("robert@example.org"));
        });
    }

    [Test]
    public async Task ABlankPasswordKeepsTheOneTheUserHas()
    {
        await Add("root", Init7TvRoles.Admin);
        var user = await Add("bob");

        await m_repository.UpdateUserAsync(Changed(user), [], null);

        Assert.That(await m_users.CheckPasswordAsync(user, Password), Is.True);
    }

    [Test]
    public async Task AGivenPasswordReplacesIt()
    {
        await Add("root", Init7TvRoles.Admin);
        var user = await Add("bob");

        var result = await m_repository.UpdateUserAsync(Changed(user), [], "Newpass1!");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(await m_users.CheckPasswordAsync(user, "Newpass1!"), Is.True);
    }

    [Test]
    public async Task AUserWhoIsNotThereIsNotFound()
    {
        // rather than a server error, which is what it used to answer
        Assert.Multiple(async () =>
        {
            Assert.That((await m_repository.GetUserByIdAsync("nobody")).ResultCode, Is.EqualTo(ResultCode.NotFound));
            Assert.That((await m_repository.GetUserByEmailAsync("nobody@example.org")).ResultCode,
                Is.EqualTo(ResultCode.NotFound));
            Assert.That((await m_repository.DeleteUserAsync("nobody")).ResultCode, Is.EqualTo(ResultCode.NotFound));
        });
    }

    [Test]
    public async Task ANonAdminIsDeletedNormally()
    {
        await Add("root", Init7TvRoles.Admin);
        var user = await Add("bob");

        var result = await m_repository.DeleteUserAsync(user.Id);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(await m_users.FindByIdAsync(user.Id), Is.Null);
    }

    [Test]
    public async Task TheSeededRolesAreTheOnesOnOffer()
    {
        Assert.That(await m_repository.GetRoleNamesAsync(), Is.EquivalentTo(Init7TvRoles.Roles));
    }
}
