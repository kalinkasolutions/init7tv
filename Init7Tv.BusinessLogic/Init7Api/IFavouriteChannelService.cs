using Init7Tv.Dto;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.Init7Api;

public interface IFavouriteChannelService
{
    /// <summary>The channel list with this viewer's stars on it. Ordering is the
    /// browser's: it re-orders on a toggle and the list is too big to ask for
    /// again just for that.</summary>
    Task<OperationResult<IReadOnlyCollection<ChannelDto>>> GetChannelsAsync(string userName);

    Task<OperationResult<bool>> SetFavouriteAsync(string userName, Guid channelId, bool isFavourite);
}
