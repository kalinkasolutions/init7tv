using Init7Tv.Dto;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.Init7Api;

public interface IChannelService
{
    Task<OperationResult<IReadOnlyCollection<ChannelDto>>> GetChannelsAsync();
    Task<OperationResult<ChannelDto>> GetChannelById(Guid channelId);
}