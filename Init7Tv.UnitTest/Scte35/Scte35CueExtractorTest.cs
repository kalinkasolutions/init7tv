using Init7Tv.BusinessLogic.Scte35;

namespace Init7Tv.UnitTest.Scte35;

/// <summary>
/// Getting cue messages out of a transport stream: finding the PID the PMT says
/// they are on, and putting a section back together across packets.
/// </summary>
public class Scte35CueExtractorTest
{
    private const int PacketSize = Scte35TestStream.PacketSize;
    private const int PmtPid = 0x1000;
    private const int VideoPid = Scte35TestStream.VideoPid;
    private const int CuePid = 0x01FD;
    private const int OtherCuePid = 0x0211;
    private const int ProgramNumber = 1;
    private const int OtherProgramNumber = 2;
    private const int OtherPmtPid = 0x1001;

    private static ulong Seconds(double value) => (ulong)(value * SpliceInfoSection.TicksPerSecond);

    private static byte[] Cue(uint eventId = 1, ulong ptsTime = 0) =>
        SpliceSectionBuilder.SpliceInsert(eventId, ptsTime: ptsTime, durationTicks: Seconds(60)).Build();

    private static byte[] Stream(params byte[][] packets) => Scte35TestStream.Concat(packets);

    [Test]
    public void TheCuePidComesFromThePmt()
    {
        var extractor = new Scte35CueExtractor();

        extractor.Read(Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid))));

        Assert.That(extractor.CuePids, Is.EqualTo(new[] { CuePid }));
    }

    [Test]
    public void ACueMessageOnThatPid_IsRead()
    {
        var extractor = new Scte35CueExtractor();

        var cues = extractor.Read(Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid)),
            Scte35TestStream.Packet(CuePid, true, Cue(eventId: 55, ptsTime: Seconds(200))))).ToArray();

        Assert.That(cues, Has.Length.EqualTo(1));
        Assert.That(cues[0].SpliceInsert!.SpliceEventId, Is.EqualTo(55));
        Assert.That(cues[0].SpliceInsert!.SpliceTime!.PtsTime, Is.EqualTo(Seconds(200)));
    }

    /// <summary>
    /// ffmpeg has no codec for a cue stream, so copying one into a capture declares it as plain
    /// private data. Reading a recording strictly by 0x86 found no cue PID at all and every
    /// recording reported no advertising.
    /// </summary>
    [Test]
    public void ACueStreamDeclaredAsPrivateData_IsReadWhenAskedFor()
    {
        var extractor = new Scte35CueExtractor(privateDataMayCarryCues: true);

        var cues = extractor.Read(Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid, cueStreamType: 0x06)),
            Scte35TestStream.Packet(CuePid, true, Cue(eventId: 7)))).ToArray();

        Assert.That(extractor.CuePids, Is.EqualTo(new[] { CuePid }));
        Assert.That(cues, Has.Length.EqualTo(1));
        Assert.That(cues[0].SpliceInsert!.SpliceEventId, Is.EqualTo(7));
    }

    /// <summary>
    /// What a capture actually holds. ffmpeg has no codec for a cue stream, so copying one wraps
    /// every section in a PES packet rather than leaving it as PSI: no pointer to step over, a start
    /// code and a header instead. Read as PSI the section began three bytes late, failed its
    /// table_id, and a recording of a channel that announces its advertising reported none.
    /// </summary>
    [Test]
    public void ACueSectionWrappedInAPesPacket_IsRead()
    {
        var extractor = new Scte35CueExtractor(privateDataMayCarryCues: true);

        var cues = extractor.Read(Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid, cueStreamType: 0x06)),
            Scte35TestStream.PesPacket(CuePid, Cue(eventId: 42)))).ToArray();

        Assert.That(cues, Has.Length.EqualTo(1));
        Assert.That(cues[0].SpliceInsert!.SpliceEventId, Is.EqualTo(42));
    }

    /// <summary>A source declares AC-3 and teletext as private data too, so reading one is strict.</summary>
    [Test]
    public void ACueStreamDeclaredAsPrivateData_IsIgnoredByDefault()
    {
        var extractor = new Scte35CueExtractor();

        var cues = extractor.Read(Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid, cueStreamType: 0x06)),
            Scte35TestStream.Packet(CuePid, true, Cue(eventId: 7)))).ToArray();

        Assert.That(extractor.CuePids, Is.Empty);
        Assert.That(cues, Is.Empty);
    }

    [Test]
    public void ACueMessageBeforeThePmtHasBeenSeen_IsNotRead()
    {
        // without the PMT there is nothing to say this PID carries cue messages,
        // and a PID carrying anything else could start with 0xFC by chance
        var extractor = new Scte35CueExtractor();

        var cues = extractor.Read(Stream(Scte35TestStream.Packet(CuePid, true, Cue())));

        Assert.That(cues, Is.Empty);
    }

    [Test]
    public void APidTheProgrammeDoesNotListAsCue_IsIgnored()
    {
        var extractor = new Scte35CueExtractor();

        var cues = extractor.Read(Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid)),
            Scte35TestStream.Packet(VideoPid, true, Cue())));

        Assert.That(cues, Is.Empty);
    }

    [Test]
    public void AProgrammeWithNoCueStream_HasNoCuePid()
    {
        var extractor = new Scte35CueExtractor();

        extractor.Read(Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid, includeCueStream: false))));

        Assert.That(extractor.CuePids, Is.Empty);
    }

    [Test]
    public void OnAMultiProgrammeStream_OnlyTheChosenProgrammesCuesAreRead()
    {
        // these multicasts carry several programmes, and the breaks of one are
        // nothing to do with the other
        var extractor = new Scte35CueExtractor(ProgramNumber);

        var cues = extractor.Read(Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid), (OtherProgramNumber, OtherPmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid)),
            Scte35TestStream.Packet(OtherPmtPid, true, Scte35TestStream.Pmt(OtherCuePid, programNumber: OtherProgramNumber)),
            Scte35TestStream.Packet(CuePid, true, Cue(eventId: 1)),
            Scte35TestStream.Packet(OtherCuePid, true, Cue(eventId: 2)))).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(extractor.CuePids, Is.EqualTo(new[] { CuePid }));
            Assert.That(cues.Select(x => x.SpliceInsert!.SpliceEventId), Is.EqualTo(new uint[] { 1 }));
        });
    }

    [Test]
    public void WithNoProgrammeChosen_EveryProgrammesCuesAreRead()
    {
        var extractor = new Scte35CueExtractor();

        var cues = extractor.Read(Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid), (OtherProgramNumber, OtherPmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid)),
            Scte35TestStream.Packet(OtherPmtPid, true, Scte35TestStream.Pmt(OtherCuePid, programNumber: OtherProgramNumber)),
            Scte35TestStream.Packet(CuePid, true, Cue(eventId: 1)),
            Scte35TestStream.Packet(OtherCuePid, true, Cue(eventId: 2)))).ToArray();

        Assert.That(cues.Select(x => x.SpliceInsert!.SpliceEventId), Is.EquivalentTo(new uint[] { 1, 2 }));
    }

    [Test]
    public void APatSplitAcrossSections_IsFollowedThrough()
    {
        // a PAT may be sent as several sections, each in its own packet. Reading
        // only the first one loses every programme announced in the rest.
        var extractor = new Scte35CueExtractor(OtherProgramNumber);

        var cues = extractor.Read(Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((OtherProgramNumber, OtherPmtPid))),
            Scte35TestStream.Packet(OtherPmtPid, true,
                Scte35TestStream.Pmt(OtherCuePid, programNumber: OtherProgramNumber)),
            Scte35TestStream.Packet(OtherCuePid, true, Cue(eventId: 3)))).ToArray();

        Assert.That(cues.Select(x => x.SpliceInsert!.SpliceEventId), Is.EqualTo(new uint[] { 3 }));
    }

    [Test]
    public void ASectionSpanningTwoPackets_IsPutBackTogether()
    {
        // a cue with a long upid does not fit in what is left of one packet
        var big = SpliceSectionBuilder
            .TimeSignal(Seconds(300))
            .WithSegmentation(1, SegmentationType.ProviderPlacementOpportunityStart,
                upid: Enumerable.Repeat((byte)0x41, 200).ToArray())
            .Build();

        Assert.That(big.Length, Is.GreaterThan(PacketSize), "the fixture has to be too big for one packet");

        var firstChunk = PacketSize - 5;
        var extractor = new Scte35CueExtractor();

        var cues = extractor.Read(Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid)),
            Scte35TestStream.Packet(CuePid, true, big.AsSpan(0, firstChunk)),
            Scte35TestStream.Packet(CuePid, false, big.AsSpan(firstChunk)))).ToArray();

        Assert.That(cues, Has.Length.EqualTo(1));
        Assert.That(cues[0].SegmentationDescriptors[0].Upid, Has.Length.EqualTo(200));
    }

    [Test]
    public void ASectionLeftHalfFinished_DoesNotSwallowTheNextOne()
    {
        // 9.6 says a section starts at the beginning of a payload, so a new start
        // means whatever was half collected is lost
        var big = SpliceSectionBuilder
            .TimeSignal(Seconds(300))
            .WithSegmentation(1, SegmentationType.BreakStart, upid: Enumerable.Repeat((byte)0x41, 200).ToArray())
            .Build();

        var extractor = new Scte35CueExtractor();

        var cues = extractor.Read(Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid)),
            Scte35TestStream.Packet(CuePid, true, big.AsSpan(0, PacketSize - 5)),     // never completed
            Scte35TestStream.Packet(CuePid, true, Cue(eventId: 77)))).ToArray();

        Assert.That(cues, Has.Length.EqualTo(1));
        Assert.That(cues[0].SpliceInsert!.SpliceEventId, Is.EqualTo(77));
    }

    [Test]
    public void APacketMarkedAsDamaged_IsDropped()
    {
        var extractor = new Scte35CueExtractor();
        var packets = Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid)),
            Scte35TestStream.Packet(CuePid, true, Cue()));

        packets[2 * PacketSize + 1] |= 0x80;      // transport_error_indicator

        Assert.That(extractor.Read(packets), Is.Empty);
    }

    [Test]
    public void APacketWithoutASyncByte_IsDropped()
    {
        var extractor = new Scte35CueExtractor();
        var packets = Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid)),
            Scte35TestStream.Packet(CuePid, true, Cue()));

        packets[2 * PacketSize] = 0x00;

        Assert.That(extractor.Read(packets), Is.Empty);
    }

    [Test]
    public void APmtThatArrivesLater_StartsTheCuesBeingRead()
    {
        // the tables repeat every few hundred milliseconds, so a stream joined mid
        // flight has cue packets before its first PMT
        var extractor = new Scte35CueExtractor();

        var cues = extractor.Read(Stream(
            Scte35TestStream.Packet(CuePid, true, Cue(eventId: 1)),
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid)),
            Scte35TestStream.Packet(CuePid, true, Cue(eventId: 2)))).ToArray();

        Assert.That(cues.Select(x => x.SpliceInsert!.SpliceEventId), Is.EqualTo(new uint[] { 2 }));
    }

    [Test]
    public void AStreamOfRandomBytes_ProducesNothingAndDoesNotThrow()
    {
        var random = new Random(99);
        var junk = new byte[PacketSize * 500];
        random.NextBytes(junk);

        for (var offset = 0; offset < junk.Length; offset += PacketSize)
        {
            junk[offset] = Scte35CueExtractor.SyncByte;
        }

        var extractor = new Scte35CueExtractor();

        Assert.DoesNotThrow(() => extractor.Read(junk));
    }

    [Test]
    public void APartialPacketAtTheEnd_IsLeftAlone()
    {
        var extractor = new Scte35CueExtractor();
        var packets = Stream(
            Scte35TestStream.Packet(0x0000, true, Scte35TestStream.Pat((ProgramNumber, PmtPid))),
            Scte35TestStream.Packet(PmtPid, true, Scte35TestStream.Pmt(CuePid)),
            Scte35TestStream.Packet(CuePid, true, Cue()));

        var cues = extractor.Read(packets.Concat(new byte[100]).ToArray()).ToArray();

        Assert.That(cues, Has.Length.EqualTo(1));
    }
}
