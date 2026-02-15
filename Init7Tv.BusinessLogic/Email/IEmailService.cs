using Init7Tv.Dto;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.Email;

public interface IEmailService
{
    Task<OperationResult<MessageDto>> SendTestMailAsync();
    Task<OperationResult<MessageDto>> SendResetPasswordMailAsync(string recipient, string email, string token);
}