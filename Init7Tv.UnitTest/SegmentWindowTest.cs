using Init7Tv.BusinessLogic.StreamManager;

namespace Init7Tv.UnitTest;

public class SegmentWindowTest
{
    private const int Keep = 3;
    private static readonly TimeSpan PerKeyframe = TimeSpan.FromSeconds(2);

    private SegmentWindow m_window = null!;

    [SetUp]
    public void SetUp()
    {
        m_window = new SegmentWindow(Keep, PerKeyframe);
    }

    private void Add(int keyframes = 1, params byte[] bytes) =>
        m_window.Add(new SegmentCut(bytes.Length == 0 ? [1] : bytes, keyframes));

    [Test]
    public void AnEmptyWindowHasNothingAndHasLostNothing()
    {
        var (segments, mediaSequenceId) = m_window.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(segments, Is.Empty);
            Assert.That(mediaSequenceId, Is.Zero);
            Assert.That(m_window.Count, Is.Zero);
        });
    }

    [Test]
    public void SegmentsAreNamedInTheOrderTheyWereCut()
    {
        Add();
        Add();

        Assert.That(m_window.Snapshot().Segments.Select(x => x.Name),
            Is.EqualTo(new[] { "seg0.ts", "seg1.ts" }));
    }

    [Test]
    public void ASegmentLastsAsLongAsTheKeyframesItHolds()
    {
        // every keyframe is one forced interval of media, which is what a player
        // needs; wall clock drifts whenever the transcode runs behind realtime
        Add(keyframes: 3);

        Assert.That(m_window.Snapshot().Segments[0].Duration, Is.EqualTo(TimeSpan.FromSeconds(6)));
    }

    [Test]
    public void TheBytesOfASegmentComeBackByName()
    {
        Add(1, 7, 8, 9);

        Assert.That(m_window.Bytes("seg0.ts"), Is.EqualTo(new byte[] { 7, 8, 9 }));
    }

    [Test]
    public void ASegmentThatWasNeverCutHasNoBytes()
    {
        Assert.That(m_window.Bytes("seg99.ts"), Is.Null);
    }

    [Test]
    public void TheWindowHoldsNoMoreThanItWasToldTo()
    {
        for (var i = 0; i < Keep + 2; i++)
        {
            Add();
        }

        Assert.That(m_window.Count, Is.EqualTo(Keep));
    }

    [Test]
    public void TheOldestSegmentIsTheOneThatFallsOff()
    {
        for (var i = 0; i < Keep + 1; i++)
        {
            Add();
        }

        Assert.That(m_window.Snapshot().Segments.Select(x => x.Name),
            Is.EqualTo(new[] { "seg1.ts", "seg2.ts", "seg3.ts" }));
    }

    [Test]
    public void BytesGoWithTheSegmentThatFellOff()
    {
        // they are held in memory, so a window that never let go would grow for
        // as long as the channel is open
        for (var i = 0; i < Keep + 1; i++)
        {
            Add();
        }

        Assert.Multiple(() =>
        {
            Assert.That(m_window.Bytes("seg0.ts"), Is.Null);
            Assert.That(m_window.Bytes("seg1.ts"), Is.Not.Null);
        });
    }

    [Test]
    public void TheMediaSequenceCountsWhatHasFallenOff()
    {
        // it is what tells a player reloading that the window moved rather than
        // that the segments it is holding were replaced
        for (var i = 0; i < Keep + 2; i++)
        {
            Add();
        }

        Assert.That(m_window.Snapshot().MediaSequenceId, Is.EqualTo(2));
    }

    [Test]
    public void TheMediaSequenceStaysAtZeroWhileNothingHasFallenOff()
    {
        Add();
        Add();

        Assert.That(m_window.Snapshot().MediaSequenceId, Is.Zero);
    }

    [Test]
    public void ASnapshotDoesNotMoveUnderTheCallerAsMoreArrive()
    {
        Add();
        var (segments, _) = m_window.Snapshot();

        Add();

        Assert.That(segments, Has.Length.EqualTo(1), "the playlist already sent out cannot change");
    }

    [Test]
    public void SegmentsArrivingFromManyThreadsAreAllAccountedFor()
    {
        // one thread cuts them while playlist requests read the window
        var window = new SegmentWindow(keep: 1000, perKeyframe: PerKeyframe);

        Parallel.For(0, 500, _ => window.Add(new SegmentCut([1], 1)));

        var (segments, _) = window.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(segments, Has.Length.EqualTo(500));
            Assert.That(segments.Select(x => x.Name).Distinct().Count(), Is.EqualTo(500),
                "no two segments took the same name");
        });
    }
}
