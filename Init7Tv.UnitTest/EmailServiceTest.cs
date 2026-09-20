using Init7Tv.BusinessLogic.Email;

namespace Init7Tv.UnitTest;

public class EmailServiceTest
{
    private const string Domain = "https://tv.example.org";

    [Test]
    public void TheLinkPointsAtTheResetPage()
    {
        var url = EmailService.ResetUrl(Domain, "bob@example.org", "token");

        Assert.That(url, Does.StartWith($"{Domain}/resetPassword.html?"));
    }

    [Test]
    public void ATokenIsEscapedSoItArrivesAsItLeft()
    {
        // Identity's tokens are base64 and routinely carry "+" and "/", and a raw
        // "+" in a query string is read back as a space: the reset then fails
        var url = EmailService.ResetUrl(Domain, "bob@example.org", "CfDJ8A+b/c=");

        Assert.Multiple(() =>
        {
            Assert.That(url, Does.Contain("token=CfDJ8A%2Bb%2Fc%3D"));
            Assert.That(url, Does.Not.Contain("+"));
        });
    }

    [Test]
    public void AnAddressIsEscapedToo()
    {
        var url = EmailService.ResetUrl(Domain, "bob+tv@example.org", "token");

        Assert.That(url, Does.Contain("email=bob%2Btv%40example.org"));
    }

    [Test]
    public void ADomainWithATrailingSlashDoesNotDoubleIt()
    {
        // a double slash in the path is a 404 behind most proxies
        var url = EmailService.ResetUrl($"{Domain}/", "bob@example.org", "token");

        Assert.That(url, Does.StartWith($"{Domain}/resetPassword.html"));
    }

    [Test]
    public void TheQueryKeepsBothPartsInTheOrderThePageReadsThem()
    {
        var url = EmailService.ResetUrl(Domain, "bob@example.org", "token");

        Assert.That(url, Is.EqualTo($"{Domain}/resetPassword.html?email=bob%40example.org&token=token"));
    }
}
