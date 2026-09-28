using Serilog;
using Serilog.Events;

namespace Init7Tv.Extensions;

public static class RequestLoggingExtension
{
    /// <summary>
    /// Serilog's one-line request summary, with the traffic that is not worth a line dropped to
    /// Verbose (and so below the configured minimum). A player fetches a playlist and a segment
    /// every few seconds for as long as it runs; left in, those lines would be nearly all of the log.
    /// </summary>
    public static IApplicationBuilder UseInit7TvRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, exception) =>
            LevelFor(context.Request.Path, context.Response.StatusCode, exception is not null));

    /// <summary>
    /// The whole rule, as a function so it can be tested without a request. Failures are always
    /// kept, whatever the path: the point of dropping a line is that nothing went wrong on it.
    /// </summary>
    public static LogEventLevel LevelFor(PathString path, int statusCode, bool failed)
    {
        if (failed || statusCode >= StatusCodes.Status500InternalServerError)
        {
            return LogEventLevel.Error;
        }

        if (statusCode >= StatusCodes.Status400BadRequest)
        {
            return LogEventLevel.Information;
        }

        // the dashboard's websocket logs once, when it closes, saying only how long it was open
        if (statusCode == StatusCodes.Status101SwitchingProtocols)
        {
            return LogEventLevel.Verbose;
        }

        return IsPlaybackOrAsset(path) ? LogEventLevel.Verbose : LogEventLevel.Information;
    }

    // live playlists and segments, the event streams that end when a tab closes, and anything with
    // a file extension: static files and a recording's .m3u8/.m4s
    private static bool IsPlaybackOrAsset(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return path.StartsWithSegments("/api/streaming/segment")
               || path.StartsWithSegments("/api/streaming/playlist")
               || value.EndsWith("/events", StringComparison.OrdinalIgnoreCase)
               || Path.HasExtension(value);
    }
}
