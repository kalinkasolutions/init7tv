using Init7Tv.Dto;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.Recording;

public interface IRecordingService
{
    Task<OperationResult<RecordingDto[]>> GetAsync(string userName, bool isAdmin);

    /// <summary>
    /// The playlist for watching it, whether it has finished or is still being written. Byte ranges
    /// of the captures on disk, so there is nothing to convert and nothing to copy.
    /// </summary>
    Task<OperationResult<string>> GetPlaylistAsync(Guid recordingId, string userName, bool isAdmin);

    /// <summary>Where the advertising falls in one recording, in seconds from its start.</summary>
    Task<OperationResult<AdBreakMark[]>> GetAdBreaksAsync(Guid recordingId, string userName, bool isAdmin);

    /// <summary>One of the captures the playlist points into, served with ranges.</summary>
    Task<OperationResult<RecordingFileDto>> GetPartAsync(
        Guid recordingId, int part, string userName, bool isAdmin);

    /// <summary>
    /// What to hand over for a download: the captures and which stretches of them to send. An mp4
    /// is made out of those as it is written to the response, because a recording is kept as the
    /// transport stream it was captured as and converting one is only worth doing when asked.
    /// </summary>
    Task<OperationResult<RecordingDownloadDto>> GetDownloadAsync(
        Guid recordingId, bool withoutAds, string userName, bool isAdmin);

    /// <summary>
    /// Ends one that is running and keeps what it caught, which is the difference between this and
    /// deleting it.
    /// </summary>
    Task<OperationResult<bool>> StopAsync(Guid recordingId, string userName, bool isAdmin);

    Task<OperationResult<bool>> DeleteAsync(Guid recordingId, string userName, bool isAdmin);

    /// <summary>
    /// What is recording right now, for the dashboard. Everybody's, because that is a question about
    /// the machine rather than about one viewer.
    /// </summary>
    Task<CurrentRecordingDto[]> GetCurrentAsync();
}
