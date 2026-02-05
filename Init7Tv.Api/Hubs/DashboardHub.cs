using Init7Tv.BusinessLogic.StreamManager;
using Microsoft.AspNetCore.SignalR;

namespace Init7Tv.Hubs;

public class DashboardHub : Hub
{
    private readonly IStreamManager m_streamManager;

    public DashboardHub(IStreamManager streamManager)
    {
        m_streamManager = streamManager;
    }

    public override async Task OnConnectedAsync()
    {
        var current = m_streamManager.GetCurrentStreams();
        await Clients.Caller.SendAsync("DashboardUpdate", current);

        await base.OnConnectedAsync();
    }
}