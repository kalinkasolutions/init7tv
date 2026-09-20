namespace Init7Tv.Dto.Settings;

public sealed class GeneralAppSettingsDto
{
    public string BaseDomain { get; set; }
    public string FfmpegLogLevel { get; set; } = "warning";
    public string FfmpegPreset { get; set; } = "ultrafast";
    public string RecordingPreset { get; set; } = "veryfast";
    public int RecordingPreRollMinutes { get; set; } = 2;
    public int RecordingPostRollMinutes { get; set; } = 5;
}
