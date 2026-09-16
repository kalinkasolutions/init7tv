using Init7Tv.Dal.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Init7Tv.Dal;

public class Init7TvContext : IdentityDbContext
{
    public DbSet<AppSettings> AppSettings { get; set; }
    public DbSet<FavouriteChannel> FavouriteChannels { get; set; }
    public DbSet<PlannedRecording> PlannedRecordings { get; set; }

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
    }
}