using System.Text.Json.Serialization;

namespace Init7Tv.Dto;

public class Init7TvChannel
{
    [JsonPropertyName("pk")]
    public Guid Pk { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("hd")]
    public bool Hd { get; set; }

    [JsonPropertyName("src")]
    public string Src { get; set; } = null!;

    [JsonPropertyName("canonical_name")]
    public string CanonicalName { get; set; } = null!;

    [JsonPropertyName("logo")]
    public string Logo { get; set; } = null!;

    [JsonPropertyName("visible")]
    public bool Visible { get; set; }

    [JsonPropertyName("ordernum")]
    public int OrderNum { get; set; }

    [JsonPropertyName("langordernum")]
    public int LangOrderNum { get; set; }

    [JsonPropertyName("country")]
    public string Country { get; set; } = null!;

    [JsonPropertyName("language")]
    public string Language { get; set; } = null!;

    [JsonPropertyName("has_replay")]
    public bool HasReplay { get; set; }

    [JsonPropertyName("hls_src")]
    public string HlsSrc { get; set; } = null!;

    [JsonPropertyName("has_hls")]
    public bool HasHls { get; set; }

    [JsonPropertyName("changed")]
    public DateTime Changed { get; set; }
}