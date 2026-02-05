using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic.StreamManager;

public interface IStreamManager
{
    Task<OperationResult<StreamDto>> StartStream(Guid channelId, int audioStreamIndex, string userName);
    OperationResult<string> GetPlaylist(string streamId, string userName);
    OperationResult<byte[]> GetSegment(string streamId, string name);
    CurrentStreamDto[] GetCurrentStreams();
}