namespace Init7Tv.Dto;

/// <summary>A record so a per-viewer copy can be made with `with`: the channel
/// list itself is cached and shared between everyone.</summary>
public sealed record ChannelDto
{
    public Guid ChannelId { get; set; }
    public string DisplayName { get; set; }
    public string MainLanguage { get; set; }
    public string CanonicalName { get; set; }
    public byte[] Logo { get; set; }
    public string HlsSource { get; set; }
    public bool ManuallyAdded { get; set; }
    public string UdpSource { get; set; }
    public bool IsFavourite { get; set; }
}
