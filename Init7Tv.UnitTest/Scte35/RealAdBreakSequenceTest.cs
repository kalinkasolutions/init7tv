using Init7Tv.BusinessLogic.Scte35;

namespace Init7Tv.UnitTest.Scte35;

/// <summary>
/// A whole advertising break as SRF 1 FHD signalled it on 16 September 2026,
/// caught on 233.50.230.1 pid 0x1fd over 45 minutes of listening. Four cue
/// messages among 2183 splice_null heartbeats.
///
/// Two breaks back to back, each announced by a splice_insert leaving the
/// network feed and closed by another carrying the same splice_event_id. Both
/// ran slightly shorter than announced, which is why the end signal decides the
/// length rather than the duration in the opening message.
/// </summary>
public class RealAdBreakSequenceTest
{
    /// <summary>16:36:40, event 0x4002e9f5 out: 256.4s of advertisements at 890.449s.</summary>
    private const string BreakOneStart =
        "fc302500000000000000fff014054002e9f57feffe04c6d8507e01601ca0a44e010100005bb0e8f6";

    /// <summary>16:40:56, event 0x4002e9f5 in: back to the programme at 1146.689s.</summary>
    private const string BreakOneEnd =
        "fc302000000000000000fff00f054002e9f57f4ffe0626bcb0a44f010100005f7d75a8";

    /// <summary>16:40:57, event 0x4002e9f6 out: another 29s from 1147.089s.</summary>
    private const string BreakTwoStart =
        "fc302500000000000000fff014054002e9f67feffe062749507e0027d350a446020000005491a28c";

    /// <summary>16:41:25, event 0x4002e9f6 in: back to the programme at 1175.809s.</summary>
    private const string BreakTwoEnd =
        "fc302000000000000000fff00f054002e9f67f4ffe064eba30a44702000000982a5d91";

    private const ulong BreakOneStartPts = 80_140_368;      // 890.449s
    private const ulong BreakOneEndPts = 103_201_968;       // 1146.689s
    private const ulong BreakTwoStartPts = 103_237_968;     // 1147.089s
    private const ulong BreakTwoEndPts = 105_822_768;       // 1175.809s

    private const uint BreakOneEventId = 0x4002E9F5;
    private const uint BreakTwoEventId = 0x4002E9F6;

    private static ulong Ticks(double seconds) => (ulong)(seconds * SpliceInfoSection.TicksPerSecond);

    private AdBreakTimeline m_timeline = null!;

    [SetUp]
    public void SetUp() => m_timeline = new AdBreakTimeline();

    private void Observe(string hex, ulong arrivalPts)
    {
        Assert.That(SpliceInfoSectionParser.TryParse(Convert.FromHexString(hex), out var cue), Is.True,
            "a cue message off the air did not parse");
        m_timeline.Observe(cue, arrivalPts);
    }

    /// <summary>Everything the channel had said by the given point on its clock.</summary>
    private void ObserveUpTo(ulong pts)
    {
        if (pts >= BreakOneStartPts - Ticks(8)) { Observe(BreakOneStart, BreakOneStartPts - Ticks(8)); }
        if (pts >= BreakOneEndPts - Ticks(1)) { Observe(BreakOneEnd, BreakOneEndPts - Ticks(1)); }
        if (pts >= BreakTwoStartPts - Ticks(1)) { Observe(BreakTwoStart, BreakTwoStartPts - Ticks(1)); }
        if (pts >= BreakTwoEndPts - Ticks(1)) { Observe(BreakTwoEnd, BreakTwoEndPts - Ticks(1)); }
    }

    [Test]
    public void EveryMessageInTheSequenceIsValid()
    {
        foreach (var hex in new[] { BreakOneStart, BreakOneEnd, BreakTwoStart, BreakTwoEnd })
        {
            var section = Convert.FromHexString(hex);

            Assert.That(Scte35Crc.Checksum(section), Is.Zero, $"the broadcaster's own CRC failed on {hex}");
            Assert.That(SpliceInfoSectionParser.TryParse(section, out var cue), Is.True);
            Assert.That(cue.CommandType, Is.EqualTo(SpliceCommandType.SpliceInsert));
        }
    }

    [Test]
    public void EveryFieldOfTheOpeningMessageIsRead()
    {
        SpliceInfoSectionParser.TryParse(Convert.FromHexString(BreakOneStart), out var cue);

        Assert.Multiple(() =>
        {
            Assert.That(cue.SpliceInsert!.SpliceEventId, Is.EqualTo(BreakOneEventId));
            Assert.That(cue.SpliceInsert.OutOfNetwork, Is.True, "leaving the network feed is what starts a break");
            Assert.That(cue.SpliceInsert.ProgramSplice, Is.True);
            Assert.That(cue.SpliceInsert.SpliceImmediate, Is.False);
            Assert.That(cue.SpliceInsert.SpliceTime!.PtsTime, Is.EqualTo(BreakOneStartPts));
            Assert.That(cue.SpliceInsert.BreakDuration!.Duration, Is.EqualTo(23_076_000));
            Assert.That(cue.SpliceInsert.BreakDuration.AutoReturn, Is.False,
                "an in point will be sent rather than it ending on its own");
            Assert.That(cue.SpliceInsert.UniqueProgramId, Is.EqualTo(0xA44E));
            Assert.That(cue.SpliceInsert.AvailNum, Is.EqualTo(1));
            Assert.That(cue.SpliceInsert.AvailsExpected, Is.EqualTo(1));
            Assert.That(cue.PtsAdjustment, Is.Zero);
            Assert.That(cue.Tier, Is.EqualTo(0xFFF));
        });
    }

    [Test]
    public void TheOutAndInPointsShareAnEventId()
    {
        // which is what lets the second one close the break the first one opened
        SpliceInfoSectionParser.TryParse(Convert.FromHexString(BreakOneStart), out var start);
        SpliceInfoSectionParser.TryParse(Convert.FromHexString(BreakOneEnd), out var end);

        Assert.Multiple(() =>
        {
            Assert.That(start.SpliceInsert!.SpliceEventId, Is.EqualTo(BreakOneEventId));
            Assert.That(end.SpliceInsert!.SpliceEventId, Is.EqualTo(BreakOneEventId));
            Assert.That(start.SpliceInsert.OutOfNetwork, Is.True);
            Assert.That(end.SpliceInsert.OutOfNetwork, Is.False);
            Assert.That(end.SpliceInsert.BreakDuration, Is.Null, "the in point carries no duration");
        });
    }

    [Test]
    public void EightSecondsBeforeItStarts_TheBreakIsPredicted()
    {
        var now = BreakOneStartPts - Ticks(8);
        ObserveUpTo(now);

        var forecast = m_timeline.Forecast(now);

        Assert.Multiple(() =>
        {
            Assert.That(forecast.InProgress, Is.Null, "the programme is still on");
            Assert.That(forecast.Next!.EventId, Is.EqualTo(BreakOneEventId));
            Assert.That(forecast.StartsIn, Is.EqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(forecast.Next.Duration!.Value.TotalSeconds, Is.EqualTo(256.4).Within(0.001));
            Assert.That(forecast.Next.AutoReturn, Is.False);
        });
    }

    [Test]
    public void AMinuteIn_ItReportsWhatIsLeft()
    {
        var now = BreakOneStartPts + Ticks(60);
        ObserveUpTo(now);

        var forecast = m_timeline.Forecast(now);

        Assert.Multiple(() =>
        {
            Assert.That(forecast.InProgress!.EventId, Is.EqualTo(BreakOneEventId));
            Assert.That(forecast.Remaining!.Value.TotalSeconds, Is.EqualTo(196.4).Within(0.001));
        });
    }

    [Test]
    public void TheInPointShortensTheBreakToWhatItActuallyRan()
    {
        // announced 256.400s, came back after 256.240s
        ObserveUpTo(BreakOneEndPts);

        var duringTheBreak = m_timeline.Forecast(BreakOneStartPts + Ticks(10));

        Assert.That(duringTheBreak.InProgress!.Duration!.Value.TotalSeconds, Is.EqualTo(256.24).Within(0.001));
    }

    [Test]
    public void BetweenTheTwoBreaks_TheSecondIsAlreadyAnnounced()
    {
        // four tenths of a second of programme between them
        var now = BreakOneEndPts + Ticks(0.2);
        ObserveUpTo(now);

        var forecast = m_timeline.Forecast(now);

        Assert.Multiple(() =>
        {
            Assert.That(forecast.InProgress, Is.Null, "the first break is over");
            Assert.That(forecast.Next!.EventId, Is.EqualTo(BreakTwoEventId));
            Assert.That(forecast.StartsIn!.Value.TotalSeconds, Is.EqualTo(0.2).Within(0.001));
        });
    }

    [Test]
    public void DuringTheSecondBreak_ItIsTheOneReported()
    {
        var now = BreakTwoStartPts + Ticks(10);
        ObserveUpTo(now);

        var forecast = m_timeline.Forecast(now);

        Assert.Multiple(() =>
        {
            Assert.That(forecast.InProgress!.EventId, Is.EqualTo(BreakTwoEventId));
            Assert.That(forecast.Remaining!.Value.TotalSeconds, Is.EqualTo(19.0).Within(0.001));
            Assert.That(forecast.Next, Is.Null);
        });
    }

    [Test]
    public void AfterBothBreaks_TheProgrammeIsBackWithNothingAnnounced()
    {
        var now = BreakTwoEndPts + Ticks(1);
        ObserveUpTo(now);

        Assert.That(m_timeline.Forecast(now), Is.EqualTo(AdBreakForecast.Empty));
    }

    [Test]
    public void BothBreaksRanShorterThanAnnounced()
    {
        // the reason an announced duration is a prediction and the in point is the
        // fact: trusting the duration would have kept the second break hidden
        SpliceInfoSectionParser.TryParse(Convert.FromHexString(BreakOneStart), out var one);
        SpliceInfoSectionParser.TryParse(Convert.FromHexString(BreakTwoStart), out var two);

        var announcedEndOne = BreakOneStartPts + one.SpliceInsert!.BreakDuration!.Duration;
        var announcedEndTwo = BreakTwoStartPts + two.SpliceInsert!.BreakDuration!.Duration;

        Assert.Multiple(() =>
        {
            Assert.That(announcedEndOne, Is.GreaterThan(BreakOneEndPts));
            Assert.That(announcedEndTwo, Is.GreaterThan(BreakTwoEndPts));
            Assert.That(SpliceInfoSection.ToTimeSpan(announcedEndOne - BreakOneEndPts).TotalSeconds,
                Is.EqualTo(0.16).Within(0.001));
            Assert.That(SpliceInfoSection.ToTimeSpan(announcedEndTwo - BreakTwoEndPts).TotalSeconds,
                Is.EqualTo(0.28).Within(0.001));
        });
    }
}
