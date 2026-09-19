namespace Init7Tv.BusinessLogic.Recording;

/// <summary>Turns the kept stretches of a capture into an mp4, writing it as it goes.</summary>
public interface IRecordingDownloadWriter
{
    Task WriteAsync(RecordingDownloadDto download, Stream into);
}
