using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic;

public interface IChannelParserService
{
    Task<OperationResult<IReadOnlyCollection<ChannelDto>>> GetChannelsAsync();
}