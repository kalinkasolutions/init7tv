using Init7Tv.BusinessLogic.Scte35;

namespace Init7Tv.UnitTest.Scte35;

/// <summary>
/// The whole path against a real stream: Data/scte35-srf1.ts is five transport
/// packets taken off SRF 1 FHD, 233.50.230.1. Its PAT, its PMT and three cue
/// packets, and no audio or video at all.
///
/// Everything else in this suite builds its own bytes, which proves the parser
/// agrees with the builder. This proves it agrees with a broadcaster.
/// </summary>
public class RealStreamTest
{
    /// <summary>The programme SRF 1 FHD is carried in.</summary>
    private const int Srf1ProgramNumber = 17201;

    /// <summary>A programme number this stream does not carry.</summary>
    private const int AbsentProgramNumber = 10;

    private const int Srf1CuePid = 0x01FD;

    private static byte[] Stream() => File.ReadAllBytes("./Data/scte35-srf1.ts");

    [Test]
    public void TheCuePidIsFoundFromTheBroadcastersOwnPmt()
    {
        var extractor = new Scte35CueExtractor();

        extractor.Read(Stream());

        Assert.That(extractor.CuePids, Is.EqualTo(new[] { Srf1CuePid }));
    }

    [Test]
    public void TheCueMessagesAreRead()
    {
        var extractor = new Scte35CueExtractor();

        var cues = extractor.Read(Stream()).ToArray();

        Assert.That(cues, Is.Not.Empty, "no cue message came out of the capture");
        Assert.Multiple(() =>
        {
            foreach (var cue in cues)
            {
                // between breaks the cue PID carries nothing but these
                Assert.That(cue.CommandType, Is.EqualTo(SpliceCommandType.SpliceNull));
                Assert.That(cue.Encrypted, Is.False);
                Assert.That(cue.PtsAdjustment, Is.Zero);
                Assert.That(cue.Tier, Is.EqualTo(0xFFF));
            }
        });
    }

    [Test]
    public void FollowingAProgrammeThisStreamDoesNotCarry_FindsNothing()
    {
        // the PAT names one programme, so asking for another must not fall back
        // to reading whatever PMT happens to go by
        var extractor = new Scte35CueExtractor(AbsentProgramNumber);

        var cues = extractor.Read(Stream());

        Assert.Multiple(() =>
        {
            Assert.That(extractor.CuePids, Is.Empty);
            Assert.That(cues, Is.Empty);
        });
    }

    [Test]
    public void FollowingTheRightProgramme_FindsItsCueStream()
    {
        var extractor = new Scte35CueExtractor(Srf1ProgramNumber);

        var cues = extractor.Read(Stream());

        Assert.Multiple(() =>
        {
            Assert.That(extractor.CuePids, Is.EqualTo(new[] { Srf1CuePid }));
            Assert.That(cues, Is.Not.Empty);
        });
    }

    [Test]
    public void ARunOfHeartbeats_PredictsNoAdBreak()
    {
        // the point of the fixture: a channel sitting quietly must not be reported
        // as being about to go to an ad
        var service = new AdBreakService();

        service.Ingest("srf1", Stream());

        Assert.That(service.Forecast("srf1", 0), Is.EqualTo(AdBreakForecast.Empty));
    }

    /// <summary>
    /// A real ad break, caught on SRF 1 FHD at 16:36:40 on 16 September 2026. A
    /// splice_insert leaving the network feed, which is a channel saying it is
    /// about to go to advertisements and how long for.
    /// </summary>
    private const string RealAdBreakCue =
        "fc302500000000000000fff014054002e9f57feffe04c6d8507e01601ca0a44e010100005bb0e8f6";

    [Test]
    public void ARealAdBreakSignal_IsRead()
    {
        Assert.That(SpliceInfoSectionParser.TryParse(Convert.FromHexString(RealAdBreakCue), out var cue), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(cue.CommandType, Is.EqualTo(SpliceCommandType.SpliceInsert));
            Assert.That(cue.SpliceInsert!.SpliceEventId, Is.EqualTo(0x4002E9F5));
            Assert.That(cue.SpliceInsert.OutOfNetwork, Is.True, "leaving the network feed is what starts a break");
            Assert.That(cue.SpliceInsert.ProgramSplice, Is.True);
            Assert.That(cue.SpliceInsert.SpliceImmediate, Is.False);
            Assert.That(cue.SpliceInsert.SpliceTime!.PtsTime, Is.EqualTo(80_140_368));
            Assert.That(cue.SpliceInsert.BreakDuration!.Duration, Is.EqualTo(23_076_000));
            Assert.That(cue.SpliceInsert.BreakDuration.AutoReturn, Is.False,
                "an in point will be sent to end it rather than it ending on its own");
            Assert.That(cue.SpliceInsert.UniqueProgramId, Is.EqualTo(0xA44E));
            Assert.That(cue.SpliceInsert.AvailNum, Is.EqualTo(1));
            Assert.That(cue.SpliceInsert.AvailsExpected, Is.EqualTo(1));
        });
    }

    [Test]
    public void ThatSignal_PredictsTheBreakBeforeItStarts()
    {
        // the whole point: the message arrives while the programme is still on
        var timeline = new AdBreakTimeline();
        SpliceInfoSectionParser.TryParse(Convert.FromHexString(RealAdBreakCue), out var cue);

        // eight seconds of warning, which is the usual pre roll
        var eightSecondsEarlier = 80_140_368UL - 8 * (ulong)SpliceInfoSection.TicksPerSecond;
        timeline.Observe(cue, eightSecondsEarlier);

        var forecast = timeline.Forecast(eightSecondsEarlier);

        Assert.Multiple(() =>
        {
            Assert.That(forecast.Next, Is.Not.Null);
            Assert.That(forecast.StartsIn, Is.EqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(forecast.Next!.Duration!.Value.TotalSeconds, Is.EqualTo(256.4).Within(0.01),
                "four minutes and sixteen seconds of advertisements");
            Assert.That(forecast.InProgress, Is.Null);
        });
    }

    [Test]
    public void ThatSignal_ReportsTheBreakWhileItRuns()
    {
        var timeline = new AdBreakTimeline();
        SpliceInfoSectionParser.TryParse(Convert.FromHexString(RealAdBreakCue), out var cue);
        timeline.Observe(cue, 80_140_368);

        // a minute into the break
        var oneMinuteIn = 80_140_368UL + 60 * (ulong)SpliceInfoSection.TicksPerSecond;
        var forecast = timeline.Forecast(oneMinuteIn);

        Assert.Multiple(() =>
        {
            Assert.That(forecast.InProgress, Is.Not.Null);
            Assert.That(forecast.Remaining!.Value.TotalSeconds, Is.EqualTo(196.4).Within(0.01));
        });
    }

    [Test]
    public void EveryPacketInTheCaptureIsWellFormed()
    {
        var stream = Stream();

        Assert.That(stream.Length % Scte35CueExtractor.PacketSize, Is.Zero, "not a whole number of packets");

        for (var offset = 0; offset < stream.Length; offset += Scte35CueExtractor.PacketSize)
        {
            Assert.That(stream[offset], Is.EqualTo(Scte35CueExtractor.SyncByte),
                $"packet at {offset} has no sync byte");
        }
    }
}
