using Init7Tv.Dal.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Init7Tv.Dal;

public class Init7TvContext : IdentityDbContext
{
    public DbSet<AppSettings> AppSettings { get; set; }
    public DbSet<FavouriteChannel> FavouriteChannels { get; set; }
    public DbSet<PlannedRecording> PlannedRecordings { get; set; }
    public DbSet<Recording> Recordings { get; set; }

    /// <summary>
    /// Sqlite hands times back with no kind at all, and an unmarked time is read
    /// as local. These ones decide when an ffmpeg stops, so a recording read an
    /// hour out is a recording that ends in the wrong place.
    /// </summary>
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        value => value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public Init7TvContext(DbContextOptions<Init7TvContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<FavouriteChannel>().HasKey(x => new { x.UserName, x.ChannelId });

        // one request per person per programme, so asking twice is not two rows
        builder.Entity<PlannedRecording>().HasKey(x => new { x.UserName, x.ProgrammeId });

        var recording = builder.Entity<Recording>();
        recording.HasKey(x => x.RecordingId);

        // the scheduler asks for everything unfinished on every tick
        recording.HasIndex(x => x.State);
        recording.HasIndex(x => new { x.UserName, x.State });
        recording.HasIndex(x => x.ProgrammeId);

        recording.Property(x => x.ScheduledStart).HasConversion(UtcConverter);
        recording.Property(x => x.ScheduledEnd).HasConversion(UtcConverter);
        recording.Property(x => x.StartedAt).HasConversion(UtcConverter);
        recording.Property(x => x.EndedAt).HasConversion(UtcConverter);
        recording.Property(x => x.CreatedAt).HasConversion(UtcConverter);
    }
}
