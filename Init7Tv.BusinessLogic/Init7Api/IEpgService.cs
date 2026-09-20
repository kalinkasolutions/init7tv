using Init7Tv.Dto;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.Init7Api;

public interface IEpgService
{
    /// <param name="daysAhead">0 for today. The guide runs about a week out.</param>
    Task<OperationResult<EpgDto[]>> GetEpg(string canonicalName, int daysAhead);
}
