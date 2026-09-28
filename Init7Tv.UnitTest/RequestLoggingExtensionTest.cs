using Init7Tv.Extensions;
using Microsoft.AspNetCore.Http;
using Serilog.Events;

namespace Init7Tv.UnitTest;

/// <summary>
/// Which requests are worth a line in the log. Playback polls far faster than anyone reads, so it
/// is dropped, but a failure is never dropped, wherever it happened.
/// </summary>
public class RequestLoggingExtensionTest
{
    [TestCase("/api/recording/planned")]
    [TestCase("/api/admin/users")]
    [TestCase("/login")]
    [TestCase("/api/streaming/start-stream")]
    public void AnOrdinaryRequestIsInformation(string path)
    {
        Assert.That(RequestLoggingExtension.LevelFor(path, StatusCodes.Status200OK, false), Is.EqualTo(LogEventLevel.Information));
    }

    [TestCase("/api/streaming/segment/abc/segment17.ts")]
    [TestCase("/api/streaming/playlist")]
    [TestCase("/api/streaming/events")]
    [TestCase("/api/recording/events")]
    [TestCase("/api/recording/recordings/5f0c2a7e-9a55-4d2e-bb8e-2b1f7c1d3e40/playlist.m3u8")]
    [TestCase("/recordings/abc/chunk-0003.m4s")]
    [TestCase("/main.js")]
    public void PlaybackAndAssetsAreDropped(string path)
    {
        Assert.That(RequestLoggingExtension.LevelFor(path, StatusCodes.Status200OK, false), Is.EqualTo(LogEventLevel.Verbose));
    }

    [Test]
    public void AClosedWebsocketIsDropped()
    {
        Assert.That(RequestLoggingExtension.LevelFor("/hub/admin/dashboard", StatusCodes.Status101SwitchingProtocols, false), Is.EqualTo(LogEventLevel.Verbose));
    }

    [Test]
    public void AClientErrorIsKeptEvenOnAPlaybackPath()
    {
        Assert.That(RequestLoggingExtension.LevelFor("/api/streaming/segment/abc/missing.ts", StatusCodes.Status404NotFound, false), Is.EqualTo(LogEventLevel.Information));
    }

    [TestCase(StatusCodes.Status500InternalServerError, false)]
    [TestCase(StatusCodes.Status200OK, true)]
    public void AFailureIsAnErrorEvenOnAPlaybackPath(int statusCode, bool failed)
    {
        Assert.That(RequestLoggingExtension.LevelFor("/api/streaming/playlist", statusCode, failed), Is.EqualTo(LogEventLevel.Error));
    }
}
