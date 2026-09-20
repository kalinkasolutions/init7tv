using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.Ffprobe;

public interface IFfprobeService
{
    /// <summary>What the source is made of, cached because probing costs about three seconds.</summary>
    Task<OperationResult<FfprobeRoot>> ProbeAsync(string streamUrl);
}
