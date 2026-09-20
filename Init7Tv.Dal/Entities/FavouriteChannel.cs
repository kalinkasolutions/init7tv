using System.ComponentModel.DataAnnotations;

namespace Init7Tv.Dal.Entities;

/// <summary>
/// A channel a viewer has starred. Keyed by channel id rather than canonical
/// name, because the SRG HD channels carry the same canonical name as the SD
/// ones they duplicate.
/// </summary>
public sealed class FavouriteChannel
{
    [MaxLength(256)]
    public string UserName { get; set; } = string.Empty;

    public Guid ChannelId { get; set; }
}
