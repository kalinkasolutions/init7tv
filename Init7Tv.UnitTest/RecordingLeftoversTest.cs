using System.Diagnostics;
using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.BusinessLogic.Recording;
using Init7Tv.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Init7Tv.UnitTest;

/// <summary>
/// Ending an ffmpeg that outlived the run which started it. Only a kill that could not be caught
/// leaves one, and it then writes on into a capture nothing is watching any more.
/// </summary>
public class RecordingLeftoversTest
{
    private string m_directory = null!;
    private RecordingEngine m_engine = null!;

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"init7tv-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);

        m_engine = new RecordingEngine(
            NullLogger<RecordingEngine>.Instance,
            new Mock<IFfprobeService>().Object,
            new Mock<IRecordingSegmentCache>().Object,
            new RecordingSignal(),
            Options.Create(new Init7TvOptions()));
    }

    [TearDown]
    public void TearDown()
    {
        m_engine.Dispose();

        if (Directory.Exists(m_directory))
        {
            Directory.Delete(m_directory, recursive: true);
        }
    }

    [Test]
    public void ADirectoryWithNothingNotedHasNothingToStop()
    {
        Assert.That(m_engine.StopLeftovers(m_directory), Is.Zero);
    }

    /// <summary>The ordinary case: the run before this one ended its ffmpeg the usual way.</summary>
    [Test]
    public void ANoteForAProcessThatIsGoneIsSimplyForgotten()
    {
        var path = RecordingFiles.PidPath(m_directory, 1);
        File.WriteAllText(path, $"{UnusedPid()} {DateTime.UtcNow:O}");

        Assert.That(m_engine.StopLeftovers(m_directory), Is.Zero);
        Assert.That(File.Exists(path), Is.False, "nothing is left for the next run to read again");
    }

    /// <summary>
    /// Pids are handed out again, so a note can end up naming something else entirely. Killing
    /// whatever inherited the number would be far worse than leaving an encode running, so the
    /// process has to be an ffmpeg that started when the note says.
    ///
    /// Proved against this very test run, which is emphatically not an ffmpeg.
    /// </summary>
    [Test]
    public void ANoteWhoseNumberNowBelongsToSomethingElseIsLeftAlone()
    {
        using var self = Process.GetCurrentProcess();
        File.WriteAllText(
            RecordingFiles.PidPath(m_directory, 1),
            $"{self.Id} {self.StartTime.ToUniversalTime():O}");

        Assert.That(m_engine.StopLeftovers(m_directory), Is.Zero);
        Assert.That(self.HasExited, Is.False, "and it is still here to say so");
    }

    [Test]
    public void AnUnreadableNoteIsNotTakenAsANumber()
    {
        var path = RecordingFiles.PidPath(m_directory, 1);
        File.WriteAllText(path, "not a pid at all");

        Assert.That(m_engine.StopLeftovers(m_directory), Is.Zero);
        Assert.That(File.Exists(path), Is.False);
    }

    /// <summary>Killed twice leaves two of them, and both have to go.</summary>
    [Test]
    public void EveryNoteInTheDirectoryIsDealtWith()
    {
        File.WriteAllText(RecordingFiles.PidPath(m_directory, 1), $"{UnusedPid()} {DateTime.UtcNow:O}");
        File.WriteAllText(RecordingFiles.PidPath(m_directory, 2), $"{UnusedPid()} {DateTime.UtcNow:O}");

        m_engine.StopLeftovers(m_directory);

        Assert.That(RecordingFiles.Pids(m_directory), Is.Empty);
    }

    /// <summary>
    /// The whole of it against a real one: an ffmpeg that would have run for another ten minutes,
    /// noted the way a capture notes its own, and ended by the run that came after it.
    /// </summary>
    [Test]
    [Explicit("Starts a real ffmpeg")]
    public void ARealFfmpegLeftBehindIsStopped()
    {
        using var ffmpeg = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            ArgumentList =
            {
                "-nostdin", "-loglevel", "quiet",
                "-f", "lavfi", "-i", "testsrc=size=320x240:rate=25",
                "-t", "600", "-f", "null", "-"
            },
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        File.WriteAllText(
            RecordingFiles.PidPath(m_directory, 1),
            $"{ffmpeg.Id} {ffmpeg.StartTime.ToUniversalTime():O}");

        Assert.That(m_engine.StopLeftovers(m_directory), Is.EqualTo(1));
        Assert.That(ffmpeg.WaitForExit(TimeSpan.FromSeconds(10)), Is.True, "and it is actually gone");
    }

    /// <summary>A number nothing is running under, which is what a note left by a dead run names.</summary>
    private static int UnusedPid()
    {
        for (var pid = 60000; pid < 65000; pid++)
        {
            try
            {
                using var _ = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                return pid;
            }
        }

        Assert.Fail("every number tried was in use");
        return 0;
    }
}
