using System.Text.Json;
using Init7Tv;
using Init7Tv.BusinessLogic;
using Init7Tv.BusinessLogic.AppSettingsService;
using Init7Tv.BusinessLogic.Email;
using Init7Tv.BusinessLogic.HttpClientWrapper;
using Init7Tv.BusinessLogic.Init7Api;
using Init7Tv.BusinessLogic.StreamEventBus;
using Init7Tv.BusinessLogic.StreamManager;
using Init7Tv.BusinessLogic.User;
using Init7Tv.Dal;
using Init7Tv.Dal.Repositories;
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
builder.Services.AddSignalR();

builder.Services.AddDbContext<Init7TvContext>(options => options.UseSqlite("Data Source=/var/srv/Init7Tv.db"));

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

    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.Redirect("/");
        return Task.CompletedTask;
    };
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
builder.Services.AddSingleton<IStreamEventBus, StreamEventBus>();

builder.Services.AddHostedService<DashboardNotifier>();

builder.Services.AddScoped<IUserIdentityProvider, UserIdentityProvider>();
builder.Services.AddScoped<IIdentityRepository, IdentityRepository>();
builder.Services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<IAppSettingsService, AppSettingsService>();
builder.Services.AddScoped<IEmailService, EmailService>();

builder.Services.AddTransient<IChannelService, ChannelService>();
builder.Services.AddTransient<IEpgService, EpgService>();

#if DEBUG
builder.WebHost.ConfigureKestrel(options => { options.ListenAnyIP(5001, listenOptions => { listenOptions.UseHttps("/home/kalinka/certs/kalinka.pfx"); }); });
#else
builder.WebHost.ConfigureKestrel(options => { options.ListenAnyIP(5001); });
#endif

var app = builder.Build();
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (BadHttpRequestException ex) when (ex.InnerException is JsonException jsonEx)
    {
        context.Response.StatusCode = 400;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            Title = jsonEx.Message,
        });
    }
});

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();


app.MapStreamingEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapAdminEndpoint();
app.MapDashboardEndpoints();
app.MapEpgEndpoints();

await Seed.InitializeAsync(app);

app.Run();