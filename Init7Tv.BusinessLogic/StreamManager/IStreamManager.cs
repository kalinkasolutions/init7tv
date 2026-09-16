using Init7Tv.Dto;
using Init7Tv.Dto.Settings;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.StreamManager;

public interface IStreamManager
{
    Task<OperationResult<StreamDto>> StartStream(Guid channelId, int audioStreamIndex, string userName, GeneralAppSettingsDto appSettings);
    OperationResult<string> GetPlaylist(string streamId, string userName);
    OperationResult<string> GetVideoPlaylist(string streamId, string userName);
    OperationResult<string> GetSubtitlePlaylist(string streamId, string userName);
    OperationResult<string> GetSubtitleSegment(string streamId, string name);
    OperationResult<byte[]> GetSegment(string streamId, string name);
    CurrentStreamDto[] GetCurrentStreams();

    /// <summary>Drops every running stream so the next start picks up new settings.</summary>
    void StopAllStreams();
}