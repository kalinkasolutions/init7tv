using Init7Tv.Dal.Repositories;
using Init7Tv.Dto;
using Init7Tv.Shared;

namespace Init7Tv.BusinessLogic.Init7Api;

public sealed class FavouriteChannelService : IFavouriteChannelService
{
    private readonly IChannelService m_channelService;
    private readonly IFavouriteChannelRepository m_favouriteChannelRepository;

    public FavouriteChannelService(
        IChannelService channelService,
        IFavouriteChannelRepository favouriteChannelRepository
    )
    {
        m_channelService = channelService;
        m_favouriteChannelRepository = favouriteChannelRepository;
    }

    public async Task<OperationResult<IReadOnlyCollection<ChannelDto>>> GetChannelsAsync(string userName)
    {
        var channelsResult = await m_channelService.GetChannelsAsync();
        if (!channelsResult.IsSuccess)
        {
            return channelsResult;
        }

        var favourites = (await m_favouriteChannelRepository.GetChannelIdsAsync(userName)).ToHashSet();

        var channels = channelsResult.Value
            .Select(x => x with { IsFavourite = favourites.Contains(x.ChannelId) })
            .ToArray();

        return OperationResult<IReadOnlyCollection<ChannelDto>>.Success(channels);
    }

    public async Task<OperationResult<bool>> SetFavouriteAsync(string userName, Guid channelId, bool isFavourite)
    {
        var channelResult = await m_channelService.GetChannelById(channelId);
        if (!channelResult.IsSuccess)
        {
            return channelResult.MapError<bool>();
        }

        await m_favouriteChannelRepository.SetAsync(userName, channelId, isFavourite);

        return OperationResult<bool>.Success(isFavourite);
    }
}
