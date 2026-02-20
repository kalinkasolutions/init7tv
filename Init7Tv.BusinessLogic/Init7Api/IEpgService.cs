using Init7Tv.Dto;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.Init7Api;

public interface IEpgService
{
    Task<OperationResult<EpgDto[]>> GetEpg(string canonicalName, bool tomorrow);
}