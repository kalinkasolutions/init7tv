using System.ComponentModel.DataAnnotations;
using Init7Tv.Shared;

namespace Init7Tv.Dal.Entities;

public sealed class AppSettings
{
    [Key]
    public int Id { get; set; }

    [MaxLength(255)]
    public string EmailFrom { get; set; } = string.Empty;

    [MaxLength(255)]
    public string SmtpHost { get; set; } = string.Empty;

    public int Port { get; set; }

    [MaxLength(255)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(255)]
    public string Password { get; set; } = string.Empty;

    public EmailSocketOptions SecureSocketOptions { get; set; }

    [MaxLength(255)]
    public string BaseDomain { get; set; } = string.Empty;

    [MaxLength(10)]
    public string FfmpegLogLevel { get; set; } = "warning";

    [MaxLength(10)]
    public string FfmpegPreset { get; set; } = "ultrafast";

    /// Live trades quality for latency; a recording is watched later and kept, so
    /// it is worth the slower preset. ultrafast measured about three times the size.
    [MaxLength(10)]
    public string RecordingPreset { get; set; } = "veryfast";

    /// Broadcasts run late far more often than early, hence the asymmetry.
    public int RecordingPreRollMinutes { get; set; } = 2;

    public int RecordingPostRollMinutes { get; set; } = 5;
}