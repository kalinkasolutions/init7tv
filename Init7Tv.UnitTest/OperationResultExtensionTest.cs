using Init7Tv.Extensions;
using Init7Tv.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Init7Tv.UnitTest;

/// <summary>
/// The one place a service's answer becomes a status code. Getting it wrong shows up as a page
/// reporting a server fault when the recording simply was not there.
/// </summary>
public class OperationResultExtensionTest
{
    private static int StatusOf(IResult result) =>
        result is IStatusCodeHttpResult status ? status.StatusCode ?? 0 : 0;

    [Test]
    public void ASuccessIsTwoHundred()
    {
        Assert.That(StatusOf(OperationResult<int>.Success(42).ToHttpResult()), Is.EqualTo(StatusCodes.Status200OK));
    }

    [TestCase(StatusCodes.Status404NotFound)]
    [TestCase(StatusCodes.Status400BadRequest)]
    [TestCase(StatusCodes.Status409Conflict)]
    [TestCase(StatusCodes.Status502BadGateway)]
    [TestCase(StatusCodes.Status500InternalServerError)]
    public void EachKindOfFailureGetsItsOwnStatus(int expected)
    {
        var result = expected switch
        {
            StatusCodes.Status404NotFound => OperationResult<int>.NotFound("why"),
            StatusCodes.Status400BadRequest => OperationResult<int>.Invalid("why"),
            StatusCodes.Status409Conflict => OperationResult<int>.Conflict("why"),
            StatusCodes.Status502BadGateway => OperationResult<int>.BadGateway("why"),
            _ => OperationResult<int>.Error("why")
        };

        Assert.That(StatusOf(result.ToHttpResult()), Is.EqualTo(expected));
    }

    [Test]
    public void AFailureCarriesItsMessageToThePage()
    {
        // the page shows this, so losing it leaves the viewer with a bare status
        var problem = (ProblemHttpResult)OperationResult<int>.NotFound("That recording was not found").ToHttpResult();

        Assert.That(problem.ProblemDetails.Title, Is.EqualTo("That recording was not found"));
    }

    [Test]
    public void TextKeepsItsContentType()
    {
        // a playlist served as text/plain is one a player will not read
        var text = (ContentHttpResult)OperationResult<string>
            .Text("#EXTM3U", "application/vnd.apple.mpegurl")
            .ToHttpResult();

        Assert.Multiple(() =>
        {
            Assert.That(text.ContentType, Is.EqualTo("application/vnd.apple.mpegurl"));
            Assert.That(text.ResponseContent, Is.EqualTo("#EXTM3U"));
        });
    }

    [Test]
    public void AFileKeepsItsContentTypeAndBytes()
    {
        var file = (FileContentHttpResult)OperationResult<byte[]>.File([1, 2, 3], "video/MP2T").ToHttpResult();

        Assert.Multiple(() =>
        {
            Assert.That(file.ContentType, Is.EqualTo("video/MP2T"));
            Assert.That(file.FileContents.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void EveryCodeIsMapped()
    {
        // the fallback throws, so a code added without a case here is a 500 on a
        // path that was working a moment ago
        foreach (var code in Enum.GetValues<ResultCode>())
        {
            Assert.That(() => Sample(code).ToHttpResult(), Throws.Nothing, $"{code}");
        }
    }

    private static OperationResult<string> Sample(ResultCode code) => code switch
    {
        ResultCode.Success => OperationResult<string>.Success("ok"),
        ResultCode.TextSuccess => OperationResult<string>.Text("ok", "text/plain"),
        ResultCode.FileResult => OperationResult<string>.File("ok", "text/plain"),
        ResultCode.NotFound => OperationResult<string>.NotFound("why"),
        ResultCode.BadGateway => OperationResult<string>.BadGateway("why"),
        ResultCode.Conflict => OperationResult<string>.Conflict("why"),
        ResultCode.Invalid => OperationResult<string>.Invalid("why"),
        _ => OperationResult<string>.Error("why")
    };
}
