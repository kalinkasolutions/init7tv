namespace Init7Tv.Dto;

public sealed class ChannelDto
{
    public Guid ChannelId { get; set; }
    public string DisplayName { get; set; }
    public string MainLanguage { get; set; }
    public string CanonicalName { get; set; }
    public byte[] Logo { get; set; }
    public string HlsSource { get; set; }
    public bool ManuallyAdded { get; set; }
    public string UdpSource { get; set; }
}