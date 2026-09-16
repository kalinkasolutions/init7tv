namespace Init7Tv.Dto.Settings;

public sealed class GeneralAppSettingsDto
{
    public string BaseDomain { get; set; }
    public string FfmpegLogLevel { get; set; } = "warning";
    public string FfmpegPreset { get; set; } = "ultrafast";
}