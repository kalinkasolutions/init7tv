using System.Text.Json.Serialization;

namespace Init7Tv.Dto;

/// <summary>A record so a per-viewer copy can be made with `with`: the channel
/// list itself is cached and shared between everyone.</summary>
public sealed record ChannelDto
{
    public Guid ChannelId { get; set; }
    public string DisplayName { get; set; }
    public string MainLanguage { get; set; }
    public string CanonicalName { get; set; }
    /// <summary>
    /// Never sent with the channel list. It is a picture that never changes, and inlining a hundred
    /// of them made that list three quarters of a megabyte of base64 that had to come down again on
    /// every page. They are fetched one at a time from an address a browser is allowed to keep.
    /// </summary>
    [JsonIgnore]
    public byte[] Logo { get; set; }
    public string HlsSource { get; set; }
    public bool ManuallyAdded { get; set; }
    public string UdpSource { get; set; }
    public bool IsFavourite { get; set; }
}
