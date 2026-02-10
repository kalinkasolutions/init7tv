using Init7Tv.Shared;

namespace Init7Tv.Dto;

public sealed class CurrentStreamDto
{
    public Guid ChannelId { get; set; }
    public string ChannelDisplayName { get; set; }
    public string[] UserNames { get; set; }
    public byte[] ChannelLogo { get; set; }
    public string Language { get; set; }
}