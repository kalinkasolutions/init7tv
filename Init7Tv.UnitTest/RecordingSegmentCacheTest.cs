using Init7Tv.BusinessLogic.Recording;

namespace Init7Tv.UnitTest;

/// <summary>
/// The index a recording keeps between requests. Scanning is what fills it, so these work on a
/// capture on disk: what is being checked is which recordings keep one and for how long.
/// </summary>
public class RecordingSegmentCacheTest
{
    /// <summary>Mirrors the bound in the cache itself, which is what these are about.</summary>
    private const int Keep = 16;

    private string m_root = null!;
    private RecordingSegmentCache m_cache = null!;

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"init7tv-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
        m_cache = new RecordingSegmentCache();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_root))
        {
            Directory.Delete(m_root, recursive: true);
        }
    }

    /// <summary>A finished capture that announces one break, so an index of it is worth something.</summary>
    private string Recording()
    {
        var directory = RecordingFiles.DirectoryFor(m_root, Guid.NewGuid());
        Directory.CreateDirectory(directory);

        var cue = Scte35.SpliceSectionBuilder
            .SpliceInsert(1, ptsTime: 1_000_000UL + 12 * TsCapture.Hz, durationTicks: 30 * TsCapture.Hz)
            .Build();

        TsCapture.Write(directory, part: 0, keyframes: 10, firstPts: 1_000_000UL, cue: (After: 1, Cue: cue));

        return directory;
    }

    private IReadOnlyList<RecordingSegment> Scan(string directory) =>
        m_cache.Segments(directory, RecordingFiles.Captures(directory), finished: true);

    [Test]
    public void AScannedRecordingHasSegments()
    {
        Assert.That(Scan(Recording()), Is.Not.Empty);
    }

    [Test]
    public void ScanningTwiceDoesNotCountTheSameSegmentsAgain()
    {
        // only the new bytes are read, and a second look at a finished capture
        // finds none: adding them again would double the playlist
        var directory = Recording();

        var first = Scan(directory);
        var second = Scan(directory);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void BreaksAreOnlyThereOnceSomethingHasScanned()
    {
        // scanning is what finds them, so asking before it has happened answers nothing
        var directory = Recording();

        Assert.That(m_cache.Breaks(directory), Is.Empty);
    }

    [Test]
    public void AForgottenRecordingKeepsNoIndex()
    {
        var directory = Recording();
        Scan(directory);

        m_cache.Forget(directory);

        Assert.That(m_cache.Breaks(directory), Is.Empty, "the index went with it");
    }

    [Test]
    public void ForgettingOneThatWasNeverThereIsHarmless()
    {
        Assert.DoesNotThrow(() => m_cache.Forget(Path.Combine(m_root, "never-scanned")));
    }

    [Test]
    public void OnlySoManyRecordingsKeepAnIndex()
    {
        // without a bound, a box that has recorded for a year holds one for every
        // recording it has ever made
        var directories = Enumerable.Range(0, Keep + 1).Select(_ => Recording()).ToArray();

        foreach (var directory in directories)
        {
            Scan(directory);
        }

        Assert.Multiple(() =>
        {
            Assert.That(m_cache.Breaks(directories[0]), Is.Empty, "the oldest was dropped");
            Assert.That(m_cache.Breaks(directories[^1]), Is.Not.Null, "the newest is still held");
        });
    }

    [Test]
    public void TheOneDroppedIsTheOneLeastRecentlyAskedAbout()
    {
        var directories = Enumerable.Range(0, Keep).Select(_ => Recording()).ToArray();
        foreach (var directory in directories)
        {
            Scan(directory);
        }

        // asking about the oldest again makes it the newest, so the next one along goes instead
        Scan(directories[0]);
        Scan(Recording());

        Assert.Multiple(() =>
        {
            Assert.That(Rescanned(directories[0]), Is.False, "still held, so nothing was rescanned");
            Assert.That(Rescanned(directories[1]), Is.True, "dropped, so it had to be read again");
        });
    }

    /// <summary>
    /// Whether the index had gone. A dropped one is rebuilt by the scan that finds it missing, and
    /// the tell is that the breaks are only there afterwards.
    /// </summary>
    private bool Rescanned(string directory)
    {
        var before = m_cache.Breaks(directory).Count;
        Scan(directory);

        return before == 0 && m_cache.Breaks(directory).Count > 0;
    }
}
