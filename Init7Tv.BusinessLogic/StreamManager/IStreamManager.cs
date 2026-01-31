using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic.StreamManager;

public interface IStreamManager
{
    Task<OperationResult<StreamDto>> StartStream(string streamUrl, int audioStreamIndex);
    OperationResult<string> GetPlaylist(string streamId);
    OperationResult<byte[]> GetSegment(string streamId, string name);
}