using Init7Tv.BusinessLogic;

namespace Init7Tv;

public static class OperationResultExtension
{
    public static IResult ToHttpResult<T>(this OperationResult<T> operationResult)
    {
        return operationResult.ResultCode switch
        {
            ResultCode.Success => operationResult.Value is not null
                ? Results.Ok(operationResult.Value)
                : Results.Ok(),
            
            ResultCode.TextSuccess => Results.Text(
                operationResult.Value as string ?? string.Empty,
                operationResult.ContentType ?? "text/plain"
            ),
            
            ResultCode.FileResult => Results.File(
                operationResult.Value as byte[] ?? [],
                operationResult.ContentType ?? "application/octet-stream"
            ),
            
            ResultCode.NotFound => Results.NotFound(new { error = operationResult.ErrorMessage }),
            
            ResultCode.BadGateway => Results.Problem(
                title: operationResult.ErrorMessage,
                statusCode: StatusCodes.Status502BadGateway
            ),

            ResultCode.Error => Results.Problem(
                title: operationResult.ErrorMessage,
                statusCode: StatusCodes.Status500InternalServerError
            ),

            _ => throw new ArgumentOutOfRangeException(
                nameof(operationResult.ResultCode),
                operationResult.ResultCode,
                "Unsupported result code"
            )
        };
    }
}