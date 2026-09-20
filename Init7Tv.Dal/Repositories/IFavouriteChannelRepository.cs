namespace Init7Tv.Dal.Repositories;

public interface IFavouriteChannelRepository
{
    Task<IReadOnlyCollection<Guid>> GetChannelIdsAsync(string userName);
    Task SetAsync(string userName, Guid channelId, bool isFavourite);
}
