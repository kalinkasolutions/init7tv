namespace Init7Tv.BusinessLogic.Scte35;

/// <summary>
/// Reads a splice_info_section, ANSI/SCTE 35 2023r1 section 9.6.
///
/// Nothing here throws: cue messages come off the air, and a damaged one is a
/// thing to drop rather than an exception to handle at every call site.
/// </summary>
public static class SpliceInfoSectionParser
{
    private const int HeaderBytes = 3;
    private const int CrcBytes = 4;
    private const byte SegmentationDescriptorTag = 0x02;

    /// <summary>"CUEI", the identifier every SCTE 35 descriptor carries.</summary>
    private const uint CueIdentifier = 0x43554549;

    public static bool TryParse(ReadOnlySpan<byte> section, out SpliceInfoSection result)
    {
        result = null!;

        if (section.Length < HeaderBytes + CrcBytes || section[0] != SpliceInfoSection.TableId)
        {
            return false;
        }

        var sectionLength = ((section[1] & 0x0F) << 8) | section[2];
        var total = HeaderBytes + sectionLength;

        // a section may arrive with the rest of the packet still attached
        if (total > section.Length || sectionLength < CrcBytes)
        {
            return false;
        }

        section = section[..total];

        if (Scte35Crc.Checksum(section) != 0)
        {
            return false;
        }

        var reader = new BitReader(section[HeaderBytes..]);

        if (!reader.TryReadBits(8, out var protocolVersion) ||
            !reader.TryReadFlag(out var encrypted) ||
            !reader.TrySkipBits(6) ||
            !reader.TryReadBits(33, out var ptsAdjustment) ||
            !reader.TrySkipBits(8) ||
            !reader.TryReadBits(12, out var tier) ||
            !reader.TryReadBits(12, out var commandLength) ||
            !reader.TryReadBits(8, out var commandType))
        {
            return false;
        }

        var parsed = new SpliceInfoSection
        {
            ProtocolVersion = (byte)protocolVersion,
            Encrypted = encrypted,
            PtsAdjustment = ptsAdjustment,
            Tier = (ushort)tier,
            CommandType = (SpliceCommandType)commandType
        };

        // The command and the descriptors sit inside the encrypted part. Everything
        // read so far is in the clear, so the message is still worth reporting.
        if (encrypted)
        {
            result = parsed;
            return true;
        }

        if (!reader.BytePosition(out var commandStart))
        {
            return false;
        }

        var body = section[(HeaderBytes + commandStart)..^CrcBytes];

        // 0xFFF means the length is unknown and the command runs to the descriptors
        var commandBytes = commandLength == 0xFFF ? -1 : (int)commandLength;
        if (commandBytes > body.Length)
        {
            return false;
        }

        switch (parsed.CommandType)
        {
            case SpliceCommandType.SpliceInsert:
                if (!TryReadSpliceInsert(body, out var insert, out var insertBytes))
                {
                    return false;
                }

                parsed = parsed with { SpliceInsert = insert };
                commandBytes = commandBytes < 0 ? insertBytes : commandBytes;
                break;

            case SpliceCommandType.TimeSignal:
                if (!TryReadSpliceTime(body, out var time, out var timeBytes))
                {
                    return false;
                }

                parsed = parsed with { TimeSignal = time };
                commandBytes = commandBytes < 0 ? timeBytes : commandBytes;
                break;

            default:
                // splice_null, splice_schedule, bandwidth_reservation and private
                // commands carry nothing this service acts on
                if (commandBytes < 0)
                {
                    result = parsed;
                    return true;
                }

                break;
        }

        var afterCommand = body[commandBytes..];
        if (afterCommand.Length < 2)
        {
            result = parsed;
            return true;
        }

        var descriptorLoopLength = (afterCommand[0] << 8) | afterCommand[1];
        var descriptors = afterCommand[2..];
        if (descriptorLoopLength > descriptors.Length)
        {
            descriptorLoopLength = descriptors.Length;
        }

        result = parsed with { SegmentationDescriptors = ReadDescriptors(descriptors[..descriptorLoopLength]) };
        return true;
    }

    private static bool TryReadSpliceInsert(ReadOnlySpan<byte> body, out SpliceInsert insert, out int bytesRead)
    {
        insert = null!;
        bytesRead = 0;

        var reader = new BitReader(body);

        if (!reader.TryReadBits(32, out var eventId) ||
            !reader.TryReadFlag(out var cancelled) ||
            !reader.TrySkipBits(7))
        {
            return false;
        }

        if (cancelled)
        {
            insert = new SpliceInsert { SpliceEventId = (uint)eventId, Cancelled = true };
            return reader.BytePosition(out bytesRead);
        }

        if (!reader.TryReadFlag(out var outOfNetwork) ||
            !reader.TryReadFlag(out var programSplice) ||
            !reader.TryReadFlag(out var durationFlag) ||
            !reader.TryReadFlag(out var spliceImmediate) ||
            !reader.TrySkipBits(4))
        {
            return false;
        }

        SpliceTime? spliceTime = null;
        if (programSplice && !spliceImmediate)
        {
            if (!reader.BytePosition(out var timeStart) ||
                !TryReadSpliceTime(body[timeStart..], out spliceTime, out var timeBytes) ||
                !reader.TrySkipBits(timeBytes * 8))
            {
                return false;
            }
        }

        if (!programSplice)
        {
            // component splice mode, deprecated: skip it rather than guess
            if (!reader.TryReadBits(8, out var componentCount))
            {
                return false;
            }

            for (var i = 0UL; i < componentCount; i++)
            {
                if (!reader.TrySkipBits(8) || (!spliceImmediate && !reader.TrySkipBits(40)))
                {
                    return false;
                }
            }
        }

        BreakDuration? breakDuration = null;
        if (durationFlag)
        {
            if (!reader.TryReadFlag(out var autoReturn) ||
                !reader.TrySkipBits(6) ||
                !reader.TryReadBits(33, out var duration))
            {
                return false;
            }

            breakDuration = new BreakDuration { AutoReturn = autoReturn, Duration = duration };
        }

        if (!reader.TryReadBits(16, out var uniqueProgramId) ||
            !reader.TryReadBits(8, out var availNum) ||
            !reader.TryReadBits(8, out var availsExpected) ||
            !reader.BytePosition(out bytesRead))
        {
            return false;
        }

        insert = new SpliceInsert
        {
            SpliceEventId = (uint)eventId,
            Cancelled = false,
            OutOfNetwork = outOfNetwork,
            ProgramSplice = programSplice,
            SpliceImmediate = spliceImmediate,
            SpliceTime = spliceTime,
            BreakDuration = breakDuration,
            UniqueProgramId = (ushort)uniqueProgramId,
            AvailNum = (byte)availNum,
            AvailsExpected = (byte)availsExpected
        };

        return true;
    }

    private static bool TryReadSpliceTime(ReadOnlySpan<byte> body, out SpliceTime? time, out int bytesRead)
    {
        time = null;
        bytesRead = 0;

        var reader = new BitReader(body);

        if (!reader.TryReadFlag(out var timeSpecified))
        {
            return false;
        }

        if (!timeSpecified)
        {
            time = new SpliceTime();
            bytesRead = 1;
            return reader.TrySkipBits(7);
        }

        if (!reader.TrySkipBits(6) || !reader.TryReadBits(33, out var pts))
        {
            return false;
        }

        time = new SpliceTime { PtsTime = pts };
        bytesRead = 5;
        return true;
    }

    private static List<SegmentationDescriptor> ReadDescriptors(ReadOnlySpan<byte> loop)
    {
        var descriptors = new List<SegmentationDescriptor>();
        var offset = 0;

        while (offset + 2 <= loop.Length)
        {
            var tag = loop[offset];
            var length = loop[offset + 1];
            var start = offset + 2;

            if (start + length > loop.Length)
            {
                break;
            }

            if (tag == SegmentationDescriptorTag &&
                TryReadSegmentationDescriptor(loop.Slice(start, length), out var descriptor))
            {
                descriptors.Add(descriptor);
            }

            offset = start + length;
        }

        return descriptors;
    }

    private static bool TryReadSegmentationDescriptor(ReadOnlySpan<byte> body, out SegmentationDescriptor descriptor)
    {
        descriptor = null!;

        var reader = new BitReader(body);

        if (!reader.TryReadBits(32, out var identifier) || identifier != CueIdentifier)
        {
            return false;
        }

        if (!reader.TryReadBits(32, out var eventId) ||
            !reader.TryReadFlag(out var cancelled) ||
            !reader.TrySkipBits(7))
        {
            return false;
        }

        if (cancelled)
        {
            descriptor = new SegmentationDescriptor { SegmentationEventId = (uint)eventId, Cancelled = true };
            return true;
        }

        if (!reader.TryReadFlag(out var programSegmentation) ||
            !reader.TryReadFlag(out var durationFlag) ||
            !reader.TryReadFlag(out var deliveryNotRestricted))
        {
            return false;
        }

        var webDeliveryAllowed = true;
        if (deliveryNotRestricted)
        {
            if (!reader.TrySkipBits(5))
            {
                return false;
            }
        }
        else
        {
            if (!reader.TryReadFlag(out webDeliveryAllowed) || !reader.TrySkipBits(4))
            {
                return false;
            }
        }

        if (!programSegmentation)
        {
            if (!reader.TryReadBits(8, out var componentCount))
            {
                return false;
            }

            for (var i = 0UL; i < componentCount; i++)
            {
                if (!reader.TrySkipBits(48))
                {
                    return false;
                }
            }
        }

        ulong? duration = null;
        if (durationFlag)
        {
            if (!reader.TryReadBits(40, out var ticks))
            {
                return false;
            }

            duration = ticks;
        }

        if (!reader.TryReadBits(8, out var upidType) ||
            !reader.TryReadBits(8, out var upidLength) ||
            !reader.TryReadBytes((int)upidLength, out var upid) ||
            !reader.TryReadBits(8, out var typeId) ||
            !reader.TryReadBits(8, out var segmentNumber) ||
            !reader.TryReadBits(8, out var segmentsExpected))
        {
            return false;
        }

        descriptor = new SegmentationDescriptor
        {
            SegmentationEventId = (uint)eventId,
            Cancelled = false,
            Type = (SegmentationType)typeId,
            Duration = duration,
            UpidType = (byte)upidType,
            Upid = upid.ToArray(),
            SegmentNumber = (byte)segmentNumber,
            SegmentsExpected = (byte)segmentsExpected,
            DeliveryNotRestricted = deliveryNotRestricted,
            WebDeliveryAllowed = webDeliveryAllowed
        };

        return true;
    }
}
