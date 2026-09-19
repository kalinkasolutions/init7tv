using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic.Recording;

/// <summary>
/// Announces what is recording, so the dashboard can say the box is busy encoding rather than only
/// counting viewers. Separate from the stream bus because that one carries streams: generalising it
/// would churn a working class for one caller.
/// </summary>
public interface IRecordingEventBus
{
    IDisposable Subscribe(Action<CurrentRecordingDto[]> handler);
    void Publish(CurrentRecordingDto[] recordings);
}
