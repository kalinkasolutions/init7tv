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
