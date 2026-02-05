using Init7Tv.BusinessLogic.StreamEventBus;
using Init7Tv.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Init7Tv.Services;

public sealed class DashboardNotifier : IHostedService
{
    private readonly IHubContext<DashboardHub> m_hubContext;
    private readonly IStreamEventBus m_bus;

    public DashboardNotifier(IHubContext<DashboardHub> hubContext, IStreamEventBus bus)
    {
        m_hubContext = hubContext;
        m_bus = bus;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        m_bus.Subscribe((stream) => { m_hubContext.Clients.All.SendAsync("DashboardUpdate", stream); });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}