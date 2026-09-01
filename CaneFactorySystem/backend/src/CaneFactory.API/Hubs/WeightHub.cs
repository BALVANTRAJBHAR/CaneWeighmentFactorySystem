using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CaneFactory.API.Hubs;

[Authorize]
public class WeightHub : Hub
{
}

public class SignalRWeightBroadcaster : ILiveWeightBroadcaster
{
    private readonly IHubContext<WeightHub> _hub;
    public SignalRWeightBroadcaster(IHubContext<WeightHub> hub) => _hub = hub;

    public Task BroadcastAsync(LiveWeightDto dto) => _hub.Clients.All.SendAsync("liveWeight", dto);
}
