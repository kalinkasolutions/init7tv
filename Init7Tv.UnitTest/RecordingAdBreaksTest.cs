using Init7Tv.BusinessLogic.Recording;
using Init7Tv.UnitTest.Scte35;

namespace Init7Tv.UnitTest;

/// <summary>
/// The cue messages are carried in the capture rather than read from the source separately, so a
/// cue and the pictures it refers to are already on the same clock. What is left is turning that
/// clock into seconds from the start of the recording, which is what the page needs to offer to
/// skip a break and what a download without the advertising leaves out.
/// </summary>
public class RecordingAdBreaksTest
{
    private const ulong Hz = TsCapture.Hz;

    private string m_directory = null!;

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"init7tv-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_directory))
        {
            Directory.Delete(m_directory, recursive: true);
        }
    }

    private void WriteCapture(int part, int keyframes, ulong firstPts, (int After, byte[] Cue)? cue = null) =>
        TsCapture.Write(m_directory, part, keyframes, firstPts, cue);

    private IReadOnlyList<Init7Tv.Dto.AdBreakMark> Read()
    {
        var index = new RecordingSegments(4.0);
        index.Extend(RecordingFiles.Captures(m_directory), finished: true);

        return index.Breaks;
    }

    /// <summary>
    /// A break is placed where its announcement went past, not at the splice time the announcement
    /// carries.
    ///
    /// The two are the same thing on a broadcast and different things in a capture, which is what
    /// this has to cope with: ffmpeg copies the cue messages out untouched, so the splice time
    /// inside them is still on the source's clock, while the pictures around them were given a
    /// clock starting near zero. Measured on a real 3+ capture the splice times read as twenty one
    /// hours into a ten minute recording. Placing them by arrival puts a break a few seconds early,
    /// by however long the broadcaster announces ahead, which is the safe direction for both a skip
    /// button and a cut.
    /// </summary>
    [Test]
    public void ABreakIsPlacedWhereItWasAnnounced()
    {
        var start = 1_000_000UL;

        // announced one keyframe in, saying it will last thirty seconds
        var cue = SpliceSectionBuilder
            .SpliceInsert(1, ptsTime: start + 12 * Hz, durationTicks: 30 * Hz)
            .Build();

        WriteCapture(part: 0, keyframes: 10, firstPts: start, cue: (After: 1, Cue: cue));

        var breaks = Read();

        Assert.That(breaks, Has.Count.EqualTo(1));
        Assert.That(breaks[0].StartsAt, Is.EqualTo(4).Within(0.5), "one keyframe in, where it was announced");
        Assert.That(breaks[0].EndsAt, Is.EqualTo(34).Within(0.5), "and as long as it said it would be");
    }

    /// <summary>
    /// Each part is its own ffmpeg run so its clock starts again, and a break in the second one has
    /// to be placed after everything the first one holds rather than back at the beginning.
    /// </summary>
    [Test]
    public void ABreakInALaterPartIsPlacedAfterTheOnesBeforeIt()
    {
        WriteCapture(part: 0, keyframes: 5, firstPts: 1_000_000UL);

        var second = 7_000_000UL;
        var cue = SpliceSectionBuilder
            .SpliceInsert(2, ptsTime: second + 8 * Hz, durationTicks: 20 * Hz)
            .Build();

        WriteCapture(part: 1, keyframes: 6, firstPts: second, cue: (After: 1, Cue: cue));

        var breaks = Read();

        Assert.That(breaks, Has.Count.EqualTo(1));

        // five keyframes of the first part, four seconds each, then one keyframe into the second
        Assert.That(breaks[0].StartsAt, Is.EqualTo(20 + 4).Within(0.5));
    }

    [Test]
    public void ACaptureWithNoCuesHasNoBreaks()
    {
        WriteCapture(part: 0, keyframes: 6, firstPts: 1_000_000UL);

        Assert.That(Read(), Is.Empty);
    }

    /// <summary>
    /// A recording that begins part way through the advertising. The cue that started the break
    /// went past before the capture did, so all it holds is the one that ends it — which is exactly
    /// the shape of a real SRF capture that began mid-break and offered nothing to skip.
    /// </summary>
    [Test]
    public void ACaptureThatBeganInsideABreakMarksItFromTheStart()
    {
        var start = 1_000_000UL;

        // returning to the network eight keyframes in, with no start to match it
        var end = SpliceSectionBuilder
            .SpliceInsert(1, outOfNetwork: false, ptsTime: start + 32 * Hz)
            .Build();

        WriteCapture(part: 0, keyframes: 15, firstPts: start, cue: (After: 8, Cue: end));

        var breaks = Read();

        Assert.That(breaks, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(breaks[0].StartsAt, Is.EqualTo(0).Within(0.5), "from the beginning of the recording");
            Assert.That(breaks[0].EndsAt, Is.EqualTo(32).Within(0.5), "to where the break was announced over");
        });
    }

    /// <summary>
    /// A start whose end never arrived says where something begins and nothing about where it
    /// stops. Assuming ten minutes marked the rest of a recording as advertising, which on a real
    /// SRF capture covered the whole programme that followed the adverts — and these marks are what
    /// the download without the advertising cuts out, so it would have dropped it.
    /// </summary>
    [Test]
    public void ABreakWhoseEndWasNeverAnnouncedIsNotOffered()
    {
        var start = 1_000_000UL;

        var open = SpliceSectionBuilder.SpliceInsert(2, ptsTime: start + 8 * Hz).Build();
        WriteCapture(part: 0, keyframes: 10, firstPts: start, cue: (After: 1, Cue: open));

        Assert.That(Read(), Is.Empty);
    }

    [Test]
    public void AnAnnouncedLengthStillStopsAtTheEndOfWhatWasRecorded()
    {
        // a recording cut short can end inside a break it knows the length of
        var start = 1_000_000UL;

        var open = SpliceSectionBuilder
            .SpliceInsert(3, ptsTime: start + 8 * Hz, durationTicks: 600 * Hz)
            .Build();

        // ten keyframes is forty seconds, far short of the ten minutes announced
        WriteCapture(part: 0, keyframes: 10, firstPts: start, cue: (After: 1, Cue: open));

        var breaks = Read();

        Assert.That(breaks, Has.Count.EqualTo(1));
        Assert.That(breaks[0].EndsAt, Is.EqualTo(40).Within(0.5));
    }

    [Test]
    public void ABreakAnnouncedAfterEverythingRecordedIsNotOffered()
    {
        // clamping must drop it rather than leave a zero length mark the page would show
        var start = 1_000_000UL;
        WriteCapture(part: 0, keyframes: 4, firstPts: start);

        Assert.That(Read(), Is.Empty);
    }
}
