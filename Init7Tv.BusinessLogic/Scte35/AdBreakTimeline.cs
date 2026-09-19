namespace Init7Tv.BusinessLogic.Scte35;

/// <summary>
/// Keeps track of the ad breaks a stream has announced and answers what is
/// coming at a given point on its clock.
///
/// Cue messages arrive seconds ahead of the break itself, which is the whole
/// point of them: the splice point is far enough away to prepare for. A break
/// is announced by either a splice_insert leaving the network feed or a
/// time_signal carrying a segmentation_descriptor, and either may be cancelled
/// or re-announced before it happens.
///
/// Not thread safe. One of these belongs to one stream.
/// </summary>
public sealed class AdBreakTimeline
{
    /// <summary>
    /// How long a break with no announced length is assumed to last when deciding
    /// whether the stream is still inside it. Breaks are minutes, not hours, and
    /// an end signal normally arrives long before this.
    /// </summary>
    public static readonly TimeSpan UnknownBreakLength = TimeSpan.FromMinutes(10);

    /// <summary>
    /// A break is forgotten this long after it ended, so a re-used event id or a
    /// late repeat of the same cue does not resurrect it.
    /// </summary>
    private static readonly TimeSpan KeepAfterEnd = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The 33 bit clock wraps every 26.5 hours. A time within half a wrap ahead
    /// counts as the future and anything else as the past, which is the usual way
    /// to read a wrapping counter and is wrong only for cues announced more than
    /// 13 hours out.
    /// </summary>
    private const ulong FutureWindow = SpliceInfoSection.PtsModulus / 2;

    private readonly Dictionary<uint, AdBreak> m_breaks = new();

    /// <summary>
    /// Takes in a cue message.
    /// </summary>
    /// <param name="section">The parsed cue.</param>
    /// <param name="arrivalPts">
    /// Where the stream is now, needed only to place a cue that says "immediately".
    /// </param>
    public void Observe(SpliceInfoSection section, ulong arrivalPts)
    {
        if (section.Encrypted)
        {
            // the command never got decoded, so there is nothing to place
            return;
        }

        if (section.SpliceInsert is { } insert)
        {
            ObserveSpliceInsert(section, insert, arrivalPts);
        }

        foreach (var descriptor in section.SegmentationDescriptors)
        {
            ObserveSegmentation(section, descriptor, arrivalPts);
        }
    }

    /// <summary>Every break announced so far, for reading a whole recording rather than a moment of one.</summary>
    public IReadOnlyCollection<AdBreak> Breaks => m_breaks.Values;

    /// <summary>What the stream has announced, seen from <paramref name="currentPts"/>.</summary>
    public AdBreakForecast Forecast(ulong currentPts)
    {
        Forget(currentPts);

        AdBreak? inProgress = null;
        TimeSpan? remaining = null;
        AdBreak? next = null;
        var soonest = ulong.MaxValue;

        foreach (var adBreak in m_breaks.Values)
        {
            var untilStart = Ahead(currentPts, adBreak.StartPts);

            if (untilStart == null)
            {
                var length = adBreak.Duration ?? UnknownBreakLength;
                var elapsed = Behind(currentPts, adBreak.StartPts);

                if (elapsed == null || elapsed >= length)
                {
                    continue;
                }

                // the one that started most recently is the one being watched
                if (inProgress == null || Behind(currentPts, inProgress.StartPts) > elapsed)
                {
                    inProgress = adBreak;
                    remaining = adBreak.Duration == null ? null : length - elapsed;
                }

                continue;
            }

            var ticks = Distance(currentPts, adBreak.StartPts);
            if (ticks < soonest)
            {
                soonest = ticks;
                next = adBreak;
            }
        }

        if (inProgress == null && next == null)
        {
            return AdBreakForecast.Empty;
        }

        return new AdBreakForecast
        {
            InProgress = inProgress,
            Remaining = remaining,
            Next = next,
            StartsIn = next == null ? null : SpliceInfoSection.ToTimeSpan(soonest)
        };
    }

    private void ObserveSpliceInsert(SpliceInfoSection section, SpliceInsert insert, ulong arrivalPts)
    {
        if (insert.Cancelled)
        {
            m_breaks.Remove(insert.SpliceEventId);
            return;
        }

        // Returning to the network feed ends a break rather than starting one. The
        // break it ends is already on the timeline with its own event id.
        if (!insert.OutOfNetwork)
        {
            EndBreakAt(insert.SpliceEventId, SpliceTimeOf(section, insert.SpliceTime, insert.SpliceImmediate, arrivalPts));
            return;
        }

        m_breaks[insert.SpliceEventId] = new AdBreak
        {
            EventId = insert.SpliceEventId,
            Signal = AdBreakSignal.SpliceInsert,
            StartPts = SpliceTimeOf(section, insert.SpliceTime, insert.SpliceImmediate, arrivalPts),
            Duration = insert.BreakDuration == null
                ? null
                : SpliceInfoSection.ToTimeSpan(insert.BreakDuration.Duration),
            AutoReturn = insert.BreakDuration?.AutoReturn ?? false
        };
    }

    private void ObserveSegmentation(SpliceInfoSection section, SegmentationDescriptor descriptor, ulong arrivalPts)
    {
        if (descriptor.Cancelled)
        {
            m_breaks.Remove(descriptor.SegmentationEventId);
            return;
        }

        var at = SpliceTimeOf(section, section.TimeSignal, spliceImmediate: false, arrivalPts);

        if (IsBreakEnd(descriptor.Type))
        {
            EndBreakAt(descriptor.SegmentationEventId, at);
            return;
        }

        if (!IsBreakStart(descriptor.Type))
        {
            return;
        }

        m_breaks[descriptor.SegmentationEventId] = new AdBreak
        {
            EventId = descriptor.SegmentationEventId,
            Signal = AdBreakSignal.Segmentation,
            StartPts = at,
            Duration = descriptor.Duration == null
                ? null
                : SpliceInfoSection.ToTimeSpan(descriptor.Duration.Value),
            SegmentationType = descriptor.Type,
            Upid = descriptor.Upid,
            WebDeliveryBlocked = !descriptor.DeliveryNotRestricted && !descriptor.WebDeliveryAllowed
        };
    }

    /// <summary>
    /// An end signal carries the same event id as the start it closes, so the
    /// break's length is what the two times say rather than what was predicted.
    /// </summary>
    private void EndBreakAt(uint eventId, ulong endPts)
    {
        if (!m_breaks.TryGetValue(eventId, out var adBreak))
        {
            return;
        }

        var length = Behind(endPts, adBreak.StartPts);
        if (length == null)
        {
            return;
        }

        m_breaks[eventId] = adBreak with { Duration = length, AutoReturn = false };
    }

    private void Forget(ulong currentPts)
    {
        var stale = m_breaks
            .Where(x =>
            {
                var elapsed = Behind(currentPts, x.Value.StartPts);
                return elapsed != null && elapsed > (x.Value.Duration ?? UnknownBreakLength) + KeepAfterEnd;
            })
            .Select(x => x.Key)
            .ToArray();

        foreach (var eventId in stale)
        {
            m_breaks.Remove(eventId);
        }
    }

    private static ulong SpliceTimeOf(SpliceInfoSection section, SpliceTime? time, bool spliceImmediate, ulong arrivalPts)
    {
        if (spliceImmediate || time?.PtsTime == null)
        {
            return arrivalPts;
        }

        return section.Adjusted(time.PtsTime.Value);
    }

    /// <summary>Distance forward from <paramref name="from"/> to <paramref name="to"/> around the wrap.</summary>
    private static ulong Distance(ulong from, ulong to) =>
        (to + SpliceInfoSection.PtsModulus - from) % SpliceInfoSection.PtsModulus;

    /// <summary>How far ahead a time is, or null when it is in the past.</summary>
    private static TimeSpan? Ahead(ulong now, ulong then)
    {
        var distance = Distance(now, then);
        return distance is > 0 and < FutureWindow ? SpliceInfoSection.ToTimeSpan(distance) : null;
    }

    /// <summary>How far back a time is, or null when it is in the future.</summary>
    private static TimeSpan? Behind(ulong now, ulong then)
    {
        var distance = Distance(then, now);
        return distance < FutureWindow ? SpliceInfoSection.ToTimeSpan(distance) : null;
    }

    private static bool IsBreakStart(SegmentationType type) => type is
        SegmentationType.BreakStart or
        SegmentationType.ProviderAdvertisementStart or
        SegmentationType.DistributorAdvertisementStart or
        SegmentationType.ProviderPlacementOpportunityStart or
        SegmentationType.DistributorPlacementOpportunityStart;

    private static bool IsBreakEnd(SegmentationType type) => type is
        SegmentationType.BreakEnd or
        SegmentationType.ProviderAdvertisementEnd or
        SegmentationType.DistributorAdvertisementEnd or
        SegmentationType.ProviderPlacementOpportunityEnd or
        SegmentationType.DistributorPlacementOpportunityEnd;
}
