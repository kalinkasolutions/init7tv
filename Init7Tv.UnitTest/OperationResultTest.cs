using Init7Tv.Shared;

namespace Init7Tv.UnitTest;

/// <summary>
/// Every service answers with one of these, and the endpoints turn the code into a status, so the
/// two things worth pinning are which codes count as failures and what survives being mapped on.
/// </summary>
public class OperationResultTest
{
    private static readonly ResultCode[] SuccessCodes =
        [ResultCode.Success, ResultCode.TextSuccess, ResultCode.FileResult];

    [Test]
    public void EveryCodeIsEitherASuccessOrAnError()
    {
        // a code added without being thought about here should read as an error:
        // treating it as a success hands the caller a Value that throws instead
        foreach (var code in Enum.GetValues<ResultCode>())
        {
            Assert.That(code.IsError(), Is.EqualTo(!SuccessCodes.Contains(code)), $"{code}");
        }
    }

    [Test]
    public void ASuccessCarriesItsValue()
    {
        var result = OperationResult<int>.Success(42);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.HasError, Is.False);
            Assert.That(result.Value, Is.EqualTo(42));
            Assert.That(result.ErrorMessage, Is.Empty);
        });
    }

    [Test]
    public void AFailureSaysSoRatherThanHandingOutANull()
    {
        var result = OperationResult<string>.NotFound("gone");

        Assert.Multiple(() =>
        {
            Assert.That(result.HasError, Is.True);
            Assert.That(result.ErrorMessage, Is.EqualTo("gone"));
            Assert.That(() => result.Value, Throws.InvalidOperationException);
        });
    }

    [Test]
    public void TheMessageOfAFailureIsInWhatItThrows()
    {
        // it is the only clue in the log about what actually went wrong
        var result = OperationResult<string>.Conflict("already recording");

        Assert.That(() => result.Value,
            Throws.InvalidOperationException.With.Message.Contains("already recording"));
    }

    [Test]
    public void TextAndFileCarryTheirContentType()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OperationResult<string>.Text("hi", "text/plain").ContentType, Is.EqualTo("text/plain"));
            Assert.That(OperationResult<byte[]>.File([1], "video/MP2T").ContentType, Is.EqualTo("video/MP2T"));
            Assert.That(OperationResult<int>.Success(1).ContentType, Is.Null);
        });
    }

    [Test]
    public void TextAndFileAreSuccessesDespiteHavingTheirOwnCode()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OperationResult<string>.Text("hi", "text/plain").IsSuccess, Is.True);
            Assert.That(OperationResult<byte[]>.File([1], "video/MP2T").IsSuccess, Is.True);
        });
    }

    [TestCase(ResultCode.NotFound)]
    [TestCase(ResultCode.BadGateway)]
    [TestCase(ResultCode.Conflict)]
    [TestCase(ResultCode.Invalid)]
    [TestCase(ResultCode.Error)]
    public void MappingAFailureOntoAnotherTypeKeepsTheCodeAndTheMessage(ResultCode code)
    {
        // services pass failures up through several layers, and a 404 arriving as
        // a 500 is the difference between "no such recording" and "we are broken"
        var mapped = Failure(code).MapError<string>();

        Assert.Multiple(() =>
        {
            Assert.That(mapped.ResultCode, Is.EqualTo(code));
            Assert.That(mapped.ErrorMessage, Is.EqualTo("why"));
        });
    }

    [Test]
    public void ASuccessCannotBeMappedAsAFailure()
    {
        Assert.That(() => OperationResult<int>.Success(1).MapError<string>(), Throws.InvalidOperationException);
    }

    [Test]
    public void ASuccessNeedsAValue()
    {
        Assert.That(() => OperationResult<string>.Success(null!), Throws.ArgumentNullException);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void AFailureNeedsSomethingToSay(string message)
    {
        // a problem response with a blank title tells the page nothing at all
        Assert.Multiple(() =>
        {
            Assert.That(() => OperationResult<int>.Error(message), Throws.ArgumentException);
            Assert.That(() => OperationResult<int>.Invalid(message), Throws.ArgumentException);
            Assert.That(() => OperationResult<int>.Conflict(message), Throws.ArgumentException);
        });
    }

    private static OperationResult<int> Failure(ResultCode code) => code switch
    {
        ResultCode.NotFound => OperationResult<int>.NotFound("why"),
        ResultCode.BadGateway => OperationResult<int>.BadGateway("why"),
        ResultCode.Conflict => OperationResult<int>.Conflict("why"),
        ResultCode.Invalid => OperationResult<int>.Invalid("why"),
        _ => OperationResult<int>.Error("why")
    };
}
