namespace Init7Tv.Dto;

public sealed class ChannelDto
{
    public Guid Pk { get; set; }
    public string DisplayName { get; set; }
    public string Language { get; set; }
    public string CanonicalName { get; set; }
    public byte[] Logo { get; set; }
    public string HlsUrl { get; set; }
}