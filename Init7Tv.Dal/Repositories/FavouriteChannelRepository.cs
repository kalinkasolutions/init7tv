using Init7Tv.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace Init7Tv.Dal.Repositories;

public sealed class FavouriteChannelRepository : IFavouriteChannelRepository
{
    private readonly Init7TvContext m_context;

    public FavouriteChannelRepository(Init7TvContext context)
    {
        m_context = context;
    }

    public async Task<IReadOnlyCollection<Guid>> GetChannelIdsAsync(string userName)
    {
        return await m_context.FavouriteChannels
            .Where(x => x.UserName == userName)
            .Select(x => x.ChannelId)
            .ToArrayAsync();
    }

    public async Task SetAsync(string userName, Guid channelId, bool isFavourite)
    {
        var existing = await m_context.FavouriteChannels
            .FirstOrDefaultAsync(x => x.UserName == userName && x.ChannelId == channelId);

        if (isFavourite == (existing != null))
        {
            return;
        }

        if (isFavourite)
        {
            m_context.FavouriteChannels.Add(new FavouriteChannel { UserName = userName, ChannelId = channelId });
        }
        else
        {
            m_context.FavouriteChannels.Remove(existing!);
        }

        await m_context.SaveChangesAsync();
    }
}
