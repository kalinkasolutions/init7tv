using Init7Tv.BusinessLogic.Recording;

namespace Init7Tv.UnitTest;

public class RecordingFilesTest
{
    private string m_root = null!;

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"init7tv-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_root))
        {
            Directory.Delete(m_root, recursive: true);
        }
    }

    /// The directory is named after the capture, which is how a finished ffmpeg
    /// is matched back to its rows.
    [Test]
    public void ADirectoryNamesTheCaptureItHolds()
    {
        var captureId = Guid.NewGuid();
        var directory = RecordingFiles.DirectoryFor(m_root, captureId);

        Assert.That(RecordingFiles.CaptureIdOf(directory), Is.EqualTo(captureId));
    }

    [Test]
    public void ADirectoryThatIsNotACaptureIsNotMistakenForOne()
    {
        Assert.That(RecordingFiles.CaptureIdOf(Path.Combine(m_root, "not-a-guid")), Is.EqualTo(Guid.Empty));
    }

    [Test]
    public void TheFirstCaptureIsPartOne()
    {
        Assert.That(RecordingFiles.NextPart(m_root), Is.EqualTo(1));
        Assert.That(RecordingFiles.Captures(m_root), Is.Empty);
    }

    /// Sorting the names as text puts part 10 before part 2, which would splice
    /// a recording back together in the wrong order.
    [Test]
    public void PartsComeBackInTheOrderTheyWereRecorded()
    {
        foreach (var part in new[] { 1, 2, 10 })
        {
            File.WriteAllText(RecordingFiles.CapturePath(m_root, part), "x");
        }

        Assert.That(
            RecordingFiles.Captures(m_root).Select(Path.GetFileName),
            Is.EqualTo(new[] { "capture-1.ts", "capture-2.ts", "capture-10.ts" }));

        Assert.That(RecordingFiles.NextPart(m_root), Is.EqualTo(11));
    }
}
