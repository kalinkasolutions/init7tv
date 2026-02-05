using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic.Init7Api;

public interface IChannelService
{
    Task<OperationResult<IReadOnlyCollection<ChannelDto>>> GetChannelsAsync();
    Task<OperationResult<ChannelDto>> GetChannelById(Guid channelId);
}