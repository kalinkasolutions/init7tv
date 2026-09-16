using Init7Tv.BusinessLogic.Scte35;

namespace Init7Tv.UnitTest.Scte35;

/// <summary>
/// Turning cue messages into "a break starts in so many seconds".
/// </summary>
public class AdBreakTimelineTest
{
    private static ulong Seconds(double value) => (ulong)(value * SpliceInfoSection.TicksPerSecond);

    private AdBreakTimeline m_timeline = null!;

    [SetUp]
    public void SetUp() => m_timeline = new AdBreakTimeline();

    private void Observe(byte[] section, ulong arrivalPts = 0)
    {
        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True, "the fixture did not parse");
        m_timeline.Observe(parsed, arrivalPts);
    }

    [Test]
    public void NothingHasBeenSignalled()
    {
        var forecast = m_timeline.Forecast(Seconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(forecast.Next, Is.Null);
            Assert.That(forecast.InProgress, Is.Null);
            Assert.That(forecast.StartsIn, Is.Null);
        });
    }

    [Test]
    public void ASpliceInsert_PredictsTheBreakAheadOfIt()
    {
        // the cue arrives at 100s and points at 108s, the usual few seconds of warning
        Observe(SpliceSectionBuilder.SpliceInsert(1, ptsTime: Seconds(108), durationTicks: Seconds(120)).Build(),
            arrivalPts: Seconds(100));

        var forecast = m_timeline.Forecast(Seconds(100));

        Assert.Multiple(() =>
        {
            Assert.That(forecast.Next!.EventId, Is.EqualTo(1));
            Assert.That(forecast.Next.Signal, Is.EqualTo(AdBreakSignal.SpliceInsert));
            Assert.That(forecast.StartsIn, Is.EqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(forecast.Next.Duration, Is.EqualTo(TimeSpan.FromMinutes(2)));
            Assert.That(forecast.InProgress, Is.Null);
        });
    }

    [Test]
    public void OnceItStarts_ItIsInProgressWithWhatIsLeftOfIt()
    {
        Observe(SpliceSectionBuilder.SpliceInsert(1, ptsTime: Seconds(108), durationTicks: Seconds(120)).Build(),
            arrivalPts: Seconds(100));

        var forecast = m_timeline.Forecast(Seconds(138));

        Assert.Multiple(() =>
        {
            Assert.That(forecast.InProgress!.EventId, Is.EqualTo(1));
            Assert.That(forecast.Remaining, Is.EqualTo(TimeSpan.FromSeconds(90)));
            Assert.That(forecast.Next, Is.Null);
        });
    }

    [Test]
    public void OnceItIsOver_ItIsNeitherComingNorRunning()
    {
        Observe(SpliceSectionBuilder.SpliceInsert(1, ptsTime: Seconds(108), durationTicks: Seconds(120)).Build(),
            arrivalPts: Seconds(100));

        var forecast = m_timeline.Forecast(Seconds(300));

        Assert.Multiple(() =>
        {
            Assert.That(forecast.InProgress, Is.Null);
            Assert.That(forecast.Next, Is.Null);
        });
    }

    [Test]
    public void AnImmediateSpliceInsert_StartsWhereTheStreamIs()
    {
        // splice_immediate carries no time at all, so the arrival point is the only
        // thing that says when the break is
        Observe(SpliceSectionBuilder.SpliceInsert(1, ptsTime: null, durationTicks: Seconds(60)).Build(),
            arrivalPts: Seconds(500));

        var forecast = m_timeline.Forecast(Seconds(500));

        Assert.Multiple(() =>
        {
            Assert.That(forecast.InProgress!.EventId, Is.EqualTo(1));
            Assert.That(forecast.Remaining, Is.EqualTo(TimeSpan.FromSeconds(60)));
        });
    }

    [Test]
    public void ACancelledEvent_IsDropped()
    {
        Observe(SpliceSectionBuilder.SpliceInsert(1, ptsTime: Seconds(200), durationTicks: Seconds(60)).Build());
        Observe(SpliceSectionBuilder.SpliceInsert(1, cancelled: true).Build());

        Assert.That(m_timeline.Forecast(Seconds(100)).Next, Is.Null);
    }

    [Test]
    public void ARepeatedEvent_MovesRatherThanDuplicates()
    {
        // a cue is sent over and over until its splice point, and the time in it
        // may be revised
        Observe(SpliceSectionBuilder.SpliceInsert(1, ptsTime: Seconds(200), durationTicks: Seconds(60)).Build());
        Observe(SpliceSectionBuilder.SpliceInsert(1, ptsTime: Seconds(240), durationTicks: Seconds(60)).Build());

        var forecast = m_timeline.Forecast(Seconds(100));

        Assert.That(forecast.StartsIn, Is.EqualTo(TimeSpan.FromSeconds(140)));
    }

    [Test]
    public void ReturningToTheNetworkFeed_EndsTheBreakItBelongsTo()
    {
        // out_of_network 0 is the in point: it closes the break, it does not open one
        Observe(SpliceSectionBuilder.SpliceInsert(1, outOfNetwork: true, ptsTime: Seconds(100)).Build());
        Observe(SpliceSectionBuilder.SpliceInsert(1, outOfNetwork: false, ptsTime: Seconds(160)).Build());

        var forecast = m_timeline.Forecast(Seconds(110));

        Assert.Multiple(() =>
        {
            Assert.That(forecast.InProgress!.EventId, Is.EqualTo(1), "still inside the break");
            Assert.That(forecast.InProgress.Duration, Is.EqualTo(TimeSpan.FromSeconds(60)),
                "the length is what the two signals say, not a guess");
            Assert.That(forecast.Remaining, Is.EqualTo(TimeSpan.FromSeconds(50)));
        });
    }

    [Test]
    public void AnInPointForSomethingNeverAnnounced_IsIgnored()
    {
        Observe(SpliceSectionBuilder.SpliceInsert(77, outOfNetwork: false, ptsTime: Seconds(160)).Build());

        Assert.That(m_timeline.Forecast(Seconds(100)).Next, Is.Null);
    }

    [TestCase(SegmentationType.BreakStart)]
    [TestCase(SegmentationType.ProviderAdvertisementStart)]
    [TestCase(SegmentationType.DistributorAdvertisementStart)]
    [TestCase(SegmentationType.ProviderPlacementOpportunityStart)]
    [TestCase(SegmentationType.DistributorPlacementOpportunityStart)]
    public void ASegmentationStart_PredictsABreak(SegmentationType type)
    {
        Observe(SpliceSectionBuilder.TimeSignal(Seconds(150))
            .WithSegmentation(9, type, durationTicks: Seconds(45))
            .Build());

        var forecast = m_timeline.Forecast(Seconds(100));

        Assert.Multiple(() =>
        {
            Assert.That(forecast.Next!.SegmentationType, Is.EqualTo(type));
            Assert.That(forecast.Next.Signal, Is.EqualTo(AdBreakSignal.Segmentation));
            Assert.That(forecast.StartsIn, Is.EqualTo(TimeSpan.FromSeconds(50)));
            Assert.That(forecast.Next.Duration, Is.EqualTo(TimeSpan.FromSeconds(45)));
        });
    }

    [TestCase(SegmentationType.ProgramStart)]
    [TestCase(SegmentationType.ProgramEnd)]
    [TestCase(SegmentationType.ProviderPromoStart)]
    [TestCase(SegmentationType.ProviderOverlayPlacementOpportunityStart)]
    [TestCase(SegmentationType.NotIndicated)]
    public void ASegmentThatIsNotAnAdBreak_IsNotOne(SegmentationType type)
    {
        // a programme boundary, a promo and an overlay all ride the same cue PID;
        // calling a programme start an ad break would be worse than saying nothing
        Observe(SpliceSectionBuilder.TimeSignal(Seconds(150))
            .WithSegmentation(9, type, durationTicks: Seconds(45))
            .Build());

        Assert.That(m_timeline.Forecast(Seconds(100)).Next, Is.Null);
    }

    [Test]
    public void ASegmentationEnd_ClosesTheBreakEarly()
    {
        // an ad break often ends before its announced length, when the programme
        // comes back early
        Observe(SpliceSectionBuilder.TimeSignal(Seconds(100))
            .WithSegmentation(9, SegmentationType.ProviderPlacementOpportunityStart, durationTicks: Seconds(180))
            .Build());
        Observe(SpliceSectionBuilder.TimeSignal(Seconds(160))
            .WithSegmentation(9, SegmentationType.ProviderPlacementOpportunityEnd)
            .Build());

        Assert.Multiple(() =>
        {
            Assert.That(m_timeline.Forecast(Seconds(150)).InProgress!.Duration, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(m_timeline.Forecast(Seconds(170)).InProgress, Is.Null, "the break is over");
        });
    }

    [Test]
    public void ACancelledSegmentationEvent_IsDropped()
    {
        Observe(SpliceSectionBuilder.TimeSignal(Seconds(150))
            .WithSegmentation(9, SegmentationType.BreakStart, durationTicks: Seconds(45))
            .Build());
        Observe(SpliceSectionBuilder.TimeSignal(Seconds(150))
            .WithSegmentation(9, SegmentationType.BreakStart, cancelled: true)
            .Build());

        Assert.That(m_timeline.Forecast(Seconds(100)).Next, Is.Null);
    }

    [Test]
    public void TheNextBreak_IsTheSoonestOfSeveral()
    {
        Observe(SpliceSectionBuilder.TimeSignal(Seconds(900))
            .WithSegmentation(1, SegmentationType.BreakStart).Build());
        Observe(SpliceSectionBuilder.TimeSignal(Seconds(300))
            .WithSegmentation(2, SegmentationType.BreakStart).Build());
        Observe(SpliceSectionBuilder.TimeSignal(Seconds(600))
            .WithSegmentation(3, SegmentationType.BreakStart).Build());

        var forecast = m_timeline.Forecast(Seconds(100));

        Assert.Multiple(() =>
        {
            Assert.That(forecast.Next!.EventId, Is.EqualTo(2));
            Assert.That(forecast.StartsIn, Is.EqualTo(TimeSpan.FromSeconds(200)));
        });
    }

    [Test]
    public void PtsAdjustment_MovesTheBreakIntoTheStreamsOwnTimeBase()
    {
        // an upstream device that restamped the clock says so here rather than
        // rewriting the times in the message
        Observe(SpliceSectionBuilder.TimeSignal(Seconds(100))
            .WithPtsAdjustment(Seconds(1000))
            .WithSegmentation(1, SegmentationType.BreakStart)
            .Build());

        Assert.That(m_timeline.Forecast(Seconds(1050)).StartsIn, Is.EqualTo(TimeSpan.FromSeconds(50)));
    }

    [Test]
    public void ABreakAcrossTheClockWrap_IsStillAhead()
    {
        // the 33 bit clock wraps every 26.5 hours, and a break signalled just before
        // the wrap points at a number smaller than the current one
        var justBeforeWrap = SpliceInfoSection.PtsModulus - Seconds(5);
        var justAfterWrap = Seconds(3);

        Observe(SpliceSectionBuilder.TimeSignal(justAfterWrap)
            .WithSegmentation(1, SegmentationType.BreakStart)
            .Build());

        var forecast = m_timeline.Forecast(justBeforeWrap);

        Assert.That(forecast.Next, Is.Not.Null, "a break after the wrap read as being in the past");
        Assert.That(forecast.StartsIn, Is.EqualTo(TimeSpan.FromSeconds(8)));
    }

    [Test]
    public void ABreakInProgressAcrossTheWrap_IsStillInProgress()
    {
        var justBeforeWrap = SpliceInfoSection.PtsModulus - Seconds(5);

        Observe(SpliceSectionBuilder.TimeSignal(justBeforeWrap)
            .WithSegmentation(1, SegmentationType.BreakStart, durationTicks: Seconds(60))
            .Build());

        var forecast = m_timeline.Forecast(Seconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(forecast.InProgress, Is.Not.Null);
            Assert.That(forecast.Remaining, Is.EqualTo(TimeSpan.FromSeconds(45)));
        });
    }

    [Test]
    public void ABreakWithNoAnnouncedLength_StopsCountingAsInProgressEventually()
    {
        // otherwise a lost end signal leaves the stream in an ad break for good
        Observe(SpliceSectionBuilder.TimeSignal(Seconds(100))
            .WithSegmentation(1, SegmentationType.BreakStart)
            .Build());

        Assert.Multiple(() =>
        {
            Assert.That(m_timeline.Forecast(Seconds(200)).InProgress, Is.Not.Null);
            Assert.That(m_timeline.Forecast(Seconds(100) + (ulong)(AdBreakTimeline.UnknownBreakLength.TotalSeconds
                * SpliceInfoSection.TicksPerSecond) + Seconds(1)).InProgress, Is.Null);
        });
    }

    [Test]
    public void ABreakWithNoAnnouncedLength_ReportsNoTimeRemaining()
    {
        // the fallback keeps it from lasting for ever; it is not a prediction
        Observe(SpliceSectionBuilder.TimeSignal(Seconds(100))
            .WithSegmentation(1, SegmentationType.BreakStart)
            .Build());

        var forecast = m_timeline.Forecast(Seconds(200));

        Assert.Multiple(() =>
        {
            Assert.That(forecast.InProgress, Is.Not.Null);
            Assert.That(forecast.Remaining, Is.Null);
        });
    }

    [Test]
    public void ARestrictedSegment_SaysSo()
    {
        // delivery restrictions decide whether an ad may be shown over the web at all
        Observe(SpliceSectionBuilder.TimeSignal(Seconds(150))
            .WithSegmentation(1, SegmentationType.ProviderAdvertisementStart,
                deliveryNotRestricted: false, webDeliveryAllowed: false)
            .Build());

        Assert.That(m_timeline.Forecast(Seconds(100)).Next!.WebDeliveryBlocked, Is.True);
    }

    [Test]
    public void AnEncryptedMessage_SignalsNothing()
    {
        // the command is inside the encrypted part, so there is no time to place
        Observe(SpliceSectionBuilder.SpliceInsert(1, ptsTime: Seconds(150), durationTicks: Seconds(60))
            .Encrypted()
            .Build());

        Assert.That(m_timeline.Forecast(Seconds(100)).Next, Is.Null);
    }

    [Test]
    public void ARealSpliceNullHeartbeat_SignalsNothing()
    {
        // what the cue PID actually carries nearly all of the time
        Observe(Convert.FromHexString("fc301100000000000000fff0000000007a4fbfff"));

        Assert.That(m_timeline.Forecast(Seconds(100)), Is.EqualTo(AdBreakForecast.Empty));
    }

    [Test]
    public void OldBreaks_DoNotPileUp()
    {
        for (var i = 1u; i <= 200; i++)
        {
            Observe(SpliceSectionBuilder.TimeSignal(Seconds(i * 10))
                .WithSegmentation(i, SegmentationType.BreakStart, durationTicks: Seconds(30))
                .Build());
        }

        // forecasting well past all of them clears them out
        var forecast = m_timeline.Forecast(Seconds(100_000));

        Assert.Multiple(() =>
        {
            Assert.That(forecast.Next, Is.Null);
            Assert.That(forecast.InProgress, Is.Null);
        });
    }
}
