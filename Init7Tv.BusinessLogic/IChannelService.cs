using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic;

public interface IChannelService
{
    Task<OperationResult<IReadOnlyCollection<ChannelDto>>> GetChannelsAsync();
}