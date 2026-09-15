using Init7Tv.BusinessLogic.User;
using Init7Tv.Dal.Repositories;
using Init7Tv.Dto.Admin;
using Init7Tv.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Init7Tv.UnitTest;

public class IdentityServiceTest
{
    private Mock<IIdentityRepository> m_repository = null!;
    private IdentityService m_service = null!;

    [SetUp]
    public void SetUp()
    {
        m_repository = new Mock<IIdentityRepository>();
        m_service = new IdentityService(NullLogger<IdentityService>.Instance, m_repository.Object);
    }

    private static AddUserDto ValidUser() => new()
    {
        UserName = "bob",
        Email = "bob@example.org",
        Password = "hunter2"
    };

    [TestCase("", "bob@example.org", "hunter2", TestName = "AddUser_RejectsAMissingUserName")]
    [TestCase("bob", "", "hunter2", TestName = "AddUser_RejectsAMissingEmail")]
    [TestCase("bob", "not-an-email", "hunter2", TestName = "AddUser_RejectsAMalformedEmail")]
    [TestCase("bob", "bob@example.org", "", TestName = "AddUser_RejectsAMissingPassword")]
    public async Task AddUser_RejectsBadInput(string userName, string email, string password)
    {
        var result = await m_service.AddUserAsync(new AddUserDto
        {
            UserName = userName,
            Email = email,
            Password = password
        });

        Assert.That(result.ResultCode, Is.EqualTo(ResultCode.Invalid));
        m_repository.Verify(
            x => x.AddUserAsync(It.IsAny<IdentityUser>(), It.IsAny<string[]>(), It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task AddUser_PassesAValidPayloadThrough()
    {
        var created = new IdentityUser { Id = "1", UserName = "bob", Email = "bob@example.org" };
        m_repository
            .Setup(x => x.AddUserAsync(It.IsAny<IdentityUser>(), It.IsAny<string[]>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult<IdentityUser>.Success(created));
        m_repository
            .Setup(x => x.GetUserByEmailAsync("bob@example.org"))
            .ReturnsAsync(OperationResult<IdentityUser>.Success(created));
        m_repository
            .Setup(x => x.GetRolesForUserAsync(created))
            .ReturnsAsync(["Admin"]);

        var result = await m_service.AddUserAsync(ValidUser());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.IsAdmin, Is.True);
    }

    [Test]
    public async Task UpdateUser_AllowsABlankPasswordToKeepTheExistingOne()
    {
        var user = new IdentityUser { Id = "1", UserName = "bob", Email = "bob@example.org" };
        m_repository
            .Setup(x => x.UpdateUserAsync(It.IsAny<IdentityUser>(), It.IsAny<string[]>(), It.IsAny<string?>()))
            .ReturnsAsync(OperationResult<IdentityUser>.Success(user));
        m_repository
            .Setup(x => x.GetUserByIdAsync("1"))
            .ReturnsAsync(OperationResult<IdentityUser>.Success(user));
        m_repository
            .Setup(x => x.GetRolesForUserAsync(user))
            .ReturnsAsync([]);

        var result = await m_service.UpdateUserAsync("1", new UpdateUserDto
        {
            UserName = "bob",
            Email = "bob@example.org",
            Password = null
        });

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task GetUsers_IncludesUsersWithoutAnEmail()
    {
        var withEmail = new IdentityUser { Id = "1", UserName = "bob", Email = "bob@example.org" };
        var withoutEmail = new IdentityUser { Id = "2", UserName = "legacy", Email = null };

        m_repository.Setup(x => x.GetUsersAsync()).ReturnsAsync([withEmail, withoutEmail]);
        m_repository.Setup(x => x.GetRolesForUserAsync(It.IsAny<IdentityUser>())).ReturnsAsync([]);

        var result = await m_service.GetUsersAsync();

        Assert.That(result.Value.Select(x => x.UserName), Is.EqualTo(new[] { "bob", "legacy" }));
    }
}
