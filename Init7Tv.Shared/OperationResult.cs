namespace Init7Tv.Shared;

public sealed class OperationResult<T>
{
    private readonly T? m_value;

    public string? ContentType { get; }
    public string ErrorMessage { get; }
    public ResultCode ResultCode { get; }

    public T Value => m_value ?? throw new InvalidOperationException($"Cannot access Value when operation failed with {ResultCode}: {ErrorMessage}");
    public bool HasError => ResultCode.IsError();
    public bool IsSuccess => !HasError;

    private OperationResult(T? value, string? contentType, string errorMessage, ResultCode resultCode)
    {
        m_value = value;
        ContentType = contentType;
        ErrorMessage = errorMessage;
        ResultCode = resultCode;
    }

    public OperationResult<TNew> MapError<TNew>()
    {
        if (IsSuccess)
        {
            throw new InvalidOperationException("Cannot map error from a successful result");
        }

        return ResultCode switch
        {
            ResultCode.NotFound => OperationResult<TNew>.NotFound(ErrorMessage),
            ResultCode.BadGateway => OperationResult<TNew>.BadGateway(ErrorMessage),
            ResultCode.Conflict => OperationResult<TNew>.Conflict(ErrorMessage),
            ResultCode.Invalid => OperationResult<TNew>.Invalid(ErrorMessage),
            _ => OperationResult<TNew>.Error(ErrorMessage)
        };
    }

    public static OperationResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new OperationResult<T>(value, null, string.Empty, ResultCode.Success);
    }

    public static OperationResult<T> Text(T text, string contentType)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        return new OperationResult<T>(text, contentType, string.Empty, ResultCode.TextSuccess);
    }

    public static OperationResult<T> File(T file, string contentType)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        return new OperationResult<T>(file, contentType, string.Empty, ResultCode.FileResult);
    }

    public static OperationResult<T> NotFound(string message = "Resource not found")
    {
        return new OperationResult<T>(default, null, message, ResultCode.NotFound);
    }

    public static OperationResult<T> BadGateway(string message = "Bad gateway")
    {
        return new OperationResult<T>(default, null, message, ResultCode.BadGateway);
    }

    /// <summary>The request payload itself is not acceptable.</summary>
    public static OperationResult<T> Invalid(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new OperationResult<T>(default, null, message, ResultCode.Invalid);
    }

    /// <summary>The request was understood but conflicts with the current state.</summary>
    public static OperationResult<T> Conflict(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new OperationResult<T>(default, null, message, ResultCode.Conflict);
    }

    public static OperationResult<T> Error(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new OperationResult<T>(default, null, message, ResultCode.Error);
    }
}

public enum ResultCode
{
    Success,
    TextSuccess,
    FileResult,
    NotFound,
    BadGateway,
    Conflict,
    Invalid,
    Error
}

public static class ResultCodeExtensions
{
    public static bool IsError(this ResultCode code) => code switch
    {
        ResultCode.NotFound => true,
        ResultCode.BadGateway => true,
        ResultCode.Conflict => true,
        ResultCode.Invalid => true,
        ResultCode.Error => true,
        _ => false
    };
}