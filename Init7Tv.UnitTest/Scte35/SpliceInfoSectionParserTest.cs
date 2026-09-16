using Init7Tv.BusinessLogic.Scte35;

namespace Init7Tv.UnitTest.Scte35;

/// <summary>
/// Reads splice_info_sections against ANSI/SCTE 35 2023r1 table 5.
/// </summary>
public class SpliceInfoSectionParserTest
{
    /// <summary>
    /// A cue message taken off SRF 1 FHD, 233.50.230.1 pid 0x1fd. A splice_null
    /// heartbeat, which is what a cue PID carries when no break is near, and the
    /// only fixture here that nothing in this repository produced.
    /// </summary>
    private const string RealSpliceNull = "fc301100000000000000fff0000000007a4fbfff";

    private static ulong Seconds(double value) => (ulong)(value * SpliceInfoSection.TicksPerSecond);

    [Test]
    public void ARealCueMessageOffTheAir_Parses()
    {
        var section = Convert.FromHexString(RealSpliceNull);

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed.CommandType, Is.EqualTo(SpliceCommandType.SpliceNull));
            Assert.That(parsed.ProtocolVersion, Is.Zero);
            Assert.That(parsed.Encrypted, Is.False);
            Assert.That(parsed.PtsAdjustment, Is.Zero);
            Assert.That(parsed.Tier, Is.EqualTo(0xFFF));
            Assert.That(parsed.SegmentationDescriptors, Is.Empty);
        });
    }

    [Test]
    public void TheCrcOfThatMessage_IsTheOneTheBroadcasterSent()
    {
        // running the checksum over a section that includes its own CRC gives zero
        Assert.That(Scte35Crc.Checksum(Convert.FromHexString(RealSpliceNull)), Is.Zero);
    }

    [Test]
    public void ASectionWithABrokenCrc_IsRejected()
    {
        var section = Convert.FromHexString(RealSpliceNull);
        section[^1] ^= 0xFF;

        Assert.That(SpliceInfoSectionParser.TryParse(section, out _), Is.False);
    }

    [Test]
    public void ASectionThatIsNotACueMessage_IsRejected()
    {
        var section = Convert.FromHexString(RealSpliceNull);
        section[0] = 0x02;                       // a PMT

        Assert.That(SpliceInfoSectionParser.TryParse(section, out _), Is.False);
    }

    [TestCase(0)]
    [TestCase(3)]
    [TestCase(6)]
    [TestCase(19)]
    public void ASectionCutShort_IsRejected(int length)
    {
        var section = Convert.FromHexString(RealSpliceNull)[..length];

        Assert.That(SpliceInfoSectionParser.TryParse(section, out _), Is.False);
    }

    [Test]
    public void TrailingBytesAfterTheSection_AreIgnored()
    {
        // a section arrives in a 188 byte packet and is padded out with stuffing
        var section = Convert.FromHexString(RealSpliceNull).Concat(Enumerable.Repeat((byte)0xFF, 60)).ToArray();

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.That(parsed.CommandType, Is.EqualTo(SpliceCommandType.SpliceNull));
    }

    [Test]
    public void SpliceInsert_LeavingTheNetworkFeed_IsReadWithItsDuration()
    {
        var section = SpliceSectionBuilder
            .SpliceInsert(eventId: 42, outOfNetwork: true, ptsTime: Seconds(1000), durationTicks: Seconds(120))
            .Build();

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed.CommandType, Is.EqualTo(SpliceCommandType.SpliceInsert));
            Assert.That(parsed.SpliceInsert!.SpliceEventId, Is.EqualTo(42));
            Assert.That(parsed.SpliceInsert.OutOfNetwork, Is.True);
            Assert.That(parsed.SpliceInsert.Cancelled, Is.False);
            Assert.That(parsed.SpliceInsert.SpliceTime!.PtsTime, Is.EqualTo(Seconds(1000)));
            Assert.That(parsed.SpliceInsert.BreakDuration!.Duration, Is.EqualTo(Seconds(120)));
            Assert.That(parsed.SpliceInsert.BreakDuration.AutoReturn, Is.True);
        });
    }

    [Test]
    public void SpliceInsert_WithNoDuration_HasNone()
    {
        var section = SpliceSectionBuilder.SpliceInsert(7, ptsTime: Seconds(5)).Build();

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.That(parsed.SpliceInsert!.BreakDuration, Is.Null);
    }

    [Test]
    public void SpliceInsert_Immediate_CarriesNoTime()
    {
        // splice_immediate_flag replaces splice_time entirely, it is not a time of zero
        var section = SpliceSectionBuilder.SpliceInsert(7, ptsTime: null).Build();

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed.SpliceInsert!.SpliceImmediate, Is.True);
            Assert.That(parsed.SpliceInsert.SpliceTime, Is.Null);
        });
    }

    [Test]
    public void SpliceInsert_Cancelled_StopsAfterTheCancelFlag()
    {
        // the rest of the command is absent when the cancel indicator is set, so
        // reading on would run into the descriptor loop
        var section = SpliceSectionBuilder.SpliceInsert(99, cancelled: true).Build();

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed.SpliceInsert!.Cancelled, Is.True);
            Assert.That(parsed.SpliceInsert.SpliceEventId, Is.EqualTo(99));
            Assert.That(parsed.SpliceInsert.BreakDuration, Is.Null);
        });
    }

    [Test]
    public void TimeSignal_WithASegmentationDescriptor_IsRead()
    {
        var section = SpliceSectionBuilder
            .TimeSignal(Seconds(3600))
            .WithSegmentation(1234, SegmentationType.ProviderPlacementOpportunityStart,
                durationTicks: Seconds(90), upid: "AD-ID"u8.ToArray())
            .Build();

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed.CommandType, Is.EqualTo(SpliceCommandType.TimeSignal));
            Assert.That(parsed.TimeSignal!.PtsTime, Is.EqualTo(Seconds(3600)));
            Assert.That(parsed.SegmentationDescriptors, Has.Count.EqualTo(1));
            Assert.That(parsed.SegmentationDescriptors[0].Type,
                Is.EqualTo(SegmentationType.ProviderPlacementOpportunityStart));
            Assert.That(parsed.SegmentationDescriptors[0].SegmentationEventId, Is.EqualTo(1234));
            Assert.That(parsed.SegmentationDescriptors[0].Duration, Is.EqualTo(Seconds(90)));
            Assert.That(parsed.SegmentationDescriptors[0].Upid, Is.EqualTo("AD-ID"u8.ToArray()));
        });
    }

    [Test]
    public void SeveralDescriptors_AreAllRead()
    {
        // a break end and an advertisement end commonly arrive in one message
        var section = SpliceSectionBuilder
            .TimeSignal(Seconds(10))
            .WithSegmentation(1, SegmentationType.ProviderAdvertisementEnd)
            .WithSegmentation(2, SegmentationType.BreakEnd)
            .Build();

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.That(parsed.SegmentationDescriptors.Select(x => x.Type), Is.EqualTo(new[]
        {
            SegmentationType.ProviderAdvertisementEnd,
            SegmentationType.BreakEnd
        }));
    }

    [Test]
    public void ADescriptorThatIsNotSegmentation_IsSkippedWithoutLosingTheNextOne()
    {
        // an avail_descriptor ahead of the one that matters: its length has to be
        // used to step over it, not a search for the next plausible tag
        var section = SpliceSectionBuilder
            .TimeSignal(Seconds(10))
            .WithDescriptor(0x00, [0x43, 0x55, 0x45, 0x49, 0x00, 0x00, 0x00, 0x01])
            .WithSegmentation(1, SegmentationType.BreakStart)
            .Build();

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.That(parsed.SegmentationDescriptors, Has.Count.EqualTo(1));
        Assert.That(parsed.SegmentationDescriptors[0].Type, Is.EqualTo(SegmentationType.BreakStart));
    }

    [Test]
    public void ASegmentationDescriptorWithoutTheCueiIdentifier_IsNotOne()
    {
        // every SCTE 35 descriptor carries "CUEI"; tag 0x02 without it belongs to
        // someone else and its bytes are not this layout
        var body = new byte[] { 0x4E, 0x4F, 0x50, 0x45 }.Concat(new byte[20]).ToArray();
        var section = SpliceSectionBuilder.TimeSignal(Seconds(10)).WithDescriptor(0x02, body).Build();

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.That(parsed.SegmentationDescriptors, Is.Empty);
    }

    [Test]
    public void PtsAdjustment_IsAddedToTheTimesInTheMessage()
    {
        var section = SpliceSectionBuilder
            .TimeSignal(Seconds(100))
            .WithPtsAdjustment(Seconds(50))
            .Build();

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed.PtsAdjustment, Is.EqualTo(Seconds(50)));
            Assert.That(parsed.Adjusted(parsed.TimeSignal!.PtsTime!.Value), Is.EqualTo(Seconds(150)));
        });
    }

    [Test]
    public void PtsAdjustment_ThatOverflows_LosesTheCarry()
    {
        // 9.6.1: "In the presence of a wrap or overflow condition, the carry shall
        // be ignored." The clock is 33 bits and wraps every 26.5 hours.
        var section = SpliceSectionBuilder
            .TimeSignal(SpliceInfoSection.PtsModulus - 10)
            .WithPtsAdjustment(100)
            .Build();

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.That(parsed.Adjusted(parsed.TimeSignal!.PtsTime!.Value), Is.EqualTo(90));
    }

    [Test]
    public void AnEncryptedMessage_ReportsWhatIsStillInTheClear()
    {
        // pts_adjustment and tier are outside the encrypted part; the command is not,
        // so guessing at it would produce a break at a made up time
        var section = SpliceSectionBuilder
            .SpliceInsert(5, ptsTime: Seconds(10))
            .WithPtsAdjustment(Seconds(7))
            .Encrypted()
            .Build();

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed.Encrypted, Is.True);
            Assert.That(parsed.PtsAdjustment, Is.EqualTo(Seconds(7)));
            Assert.That(parsed.SpliceInsert, Is.Null);
        });
    }

    [Test]
    public void ACommandThisServiceDoesNotActOn_StillParses()
    {
        // bandwidth_reservation and private commands go by regularly and must not
        // stop the descriptors after them being read
        var section = SpliceSectionBuilder.SpliceNull()
            .WithSegmentation(8, SegmentationType.ProgramStart)
            .Build();

        Assert.That(SpliceInfoSectionParser.TryParse(section, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed.CommandType, Is.EqualTo(SpliceCommandType.SpliceNull));
            Assert.That(parsed.SegmentationDescriptors, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void RandomBytes_AreNeverAccepted()
    {
        // a cue PID can carry anything once the stream is damaged
        var random = new Random(1234);

        for (var i = 0; i < 2000; i++)
        {
            var junk = new byte[random.Next(0, 200)];
            random.NextBytes(junk);

            if (junk.Length > 0)
            {
                junk[0] = SpliceInfoSection.TableId;
            }

            Assert.DoesNotThrow(() => SpliceInfoSectionParser.TryParse(junk, out _));
        }
    }

    [Test]
    public void EveryTruncationOfAValidMessage_IsRejectedRatherThanMisread()
    {
        var section = SpliceSectionBuilder
            .TimeSignal(Seconds(10))
            .WithSegmentation(1, SegmentationType.BreakStart, durationTicks: Seconds(60))
            .Build();

        for (var length = 0; length < section.Length; length++)
        {
            var truncated = section[..length];

            Assert.That(SpliceInfoSectionParser.TryParse(truncated, out _), Is.False,
                $"a message cut to {length} bytes was accepted");
        }
    }

    [Test]
    public void TicksConvertToTime_AtNinetyKilohertz()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SpliceInfoSection.ToTimeSpan(90_000), Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(SpliceInfoSection.ToTimeSpan(45_000), Is.EqualTo(TimeSpan.FromMilliseconds(500)));
            Assert.That(SpliceInfoSection.ToTimeSpan(0), Is.EqualTo(TimeSpan.Zero));
        });
    }
}
