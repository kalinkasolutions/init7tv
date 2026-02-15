namespace Init7Tv.Dto;

public sealed class ChannelDto
{
    public Guid ChannelId { get; set; }
    public string DisplayName { get; set; }
    public string MainLaunguage { get; set; }
    public string CanonicalName { get; set; }
    public byte[] Logo { get; set; }
    public string HlsUrl { get; set; }
    public bool ManuallyAdded { get; set; }
}