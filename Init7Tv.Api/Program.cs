using Init7Tv;
using Init7Tv.BusinessLogic;
using Init7Tv.Dal;
using Init7Tv.Endpoints;
using Init7Tv.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

using var loggerFactory = LoggerFactory.Create(loggingBuilder => { loggingBuilder.AddConsole(); });

builder.Services.AddMemoryCache();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAntiforgery();

builder.Services.AddDbContext<Init7TvContext>(options => options.UseSqlite("Data Source=Init7Tv.db"));

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequiredLength = 4;
    })
    .AddEntityFrameworkStores<Init7TvContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "Init7Tv.Auth";
    options.LoginPath = "/login.html";
    options.LogoutPath = "/login.html";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login.html";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization();

builder.Services.AddHttpClient<IHttpClientWrapper, HttpClientWrapper>();

builder.Services.AddSingleton<IStreamManager, StreamManager>();
builder.Services.AddScoped<IUserIdentityProvider, UserIdentityProvider>();
builder.Services.AddTransient<IChannelService, ChannelService>();

#if DEBUG
builder.WebHost.ConfigureKestrel(options => { options.ListenAnyIP(5001, listenOptions => { listenOptions.UseHttps("/home/kalinka/certs/kalinka.pfx"); }); });
#endif

var app = builder.Build();

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapStreamingEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();

await Seed.Initialize(app);

app.Run();