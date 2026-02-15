using Init7Tv.Dal.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Init7Tv.Dal;

public class Init7TvContext : IdentityDbContext
{
    public DbSet<AppSettings> AppSettings { get; set; }

    public Init7TvContext(DbContextOptions<Init7TvContext> options)
        : base(options)
    {
    }
}