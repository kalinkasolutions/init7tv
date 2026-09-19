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

    /// <summary>One of the captures the playlist points into, served with ranges.</summary>
    Task<OperationResult<RecordingFileDto>> GetPartAsync(
        Guid recordingId, int part, string userName, bool isAdmin);

    /// <summary>Where the file is and what to call it, never the file itself.</summary>
    Task<OperationResult<RecordingFileDto>> GetFileAsync(Guid recordingId, string userName, bool isAdmin);

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
