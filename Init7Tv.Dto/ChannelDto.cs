namespace Init7Tv.Dto;

public class ChannelDto
{
    public Guid Pk { get; set; }
    public string DisplayName { get; set; }
    public string Language { get; set; }
    public string CannonicalName { get; set; }
    public byte[] Logo { get; set; }
    public string HlsUrl { get; set; }
}