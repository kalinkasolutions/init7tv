using Init7Tv.BusinessLogic.Recording;
using Init7Tv.BusinessLogic.StreamManager;
using Microsoft.AspNetCore.SignalR;

namespace Init7Tv.Hubs;

public class DashboardHub : Hub
{
    private readonly IStreamManager m_streamManager;
    private readonly IRecordingService m_recordingService;

    public DashboardHub(IStreamManager streamManager, IRecordingService recordingService)
    {
        m_streamManager = streamManager;
        m_recordingService = recordingService;
    }

    public override async Task OnConnectedAsync()
    {
        var current = m_streamManager.GetCurrentStreams();
        await Clients.Caller.SendAsync("DashboardUpdate", current);

        // a pass only publishes when something changes, so one that arrives between them would
        // otherwise see nothing until the next recording started or ended
        await Clients.Caller.SendAsync("RecordingUpdate", await m_recordingService.GetCurrentAsync());

        await base.OnConnectedAsync();
    }
}