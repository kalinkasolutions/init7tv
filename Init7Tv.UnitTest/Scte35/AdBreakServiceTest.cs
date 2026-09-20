using Init7Tv.BusinessLogic.Scte35;

namespace Init7Tv.UnitTest.Scte35;

/// <summary>
/// The per stream book keeping around <see cref="AdBreakTimeline"/>.
/// </summary>
public class AdBreakServiceTest
{
    private const int PmtPid = 0x1000;
    private const int CuePid = 0x01FD;

    private static ulong Seconds(double value) => (ulong)(value * SpliceInfoSection.TicksPerSecond);

    private AdBreakService m_service = null!;

    [SetUp]
    public void SetUp() => m_service = new AdBreakService();

    /// <summary>A stream announcing one break, as transport packets.</summary>
    private static byte[] StreamAnnouncing(uint eventId, ulong startPts, ulong durationTicks)
    {
        var cue = SpliceSectionBuilder
            .SpliceInsert(eventId, ptsTime: startPts, durationTicks: durationTicks)
            .Build();

        return Scte35TestStream.Build(PmtPid, CuePid, cue);
    }

    [Test]
    public void AStreamNobodyHasFed_ForecastsNothing()
    {
        Assert.That(m_service.Forecast("unknown", Seconds(10)), Is.EqualTo(AdBreakForecast.Empty));
    }

    [Test]
    public void ABreakAnnouncedInTheStream_TurnsUpInTheForecast()
    {
        m_service.Ingest("stream-a", StreamAnnouncing(1, Seconds(200), Seconds(60)));

        var forecast = m_service.Forecast("stream-a", Seconds(150));

        Assert.Multiple(() =>
        {
            Assert.That(forecast.Next!.EventId, Is.EqualTo(1));
            Assert.That(forecast.StartsIn, Is.EqualTo(TimeSpan.FromSeconds(50)));
        });
    }

    [Test]
    public void OneStreamsBreaks_AreNotAnothers()
    {
        m_service.Ingest("stream-a", StreamAnnouncing(1, Seconds(200), Seconds(60)));
        m_service.Ingest("stream-b", StreamAnnouncing(2, Seconds(500), Seconds(60)));

        Assert.Multiple(() =>
        {
            Assert.That(m_service.Forecast("stream-a", Seconds(150)).Next!.EventId, Is.EqualTo(1));
            Assert.That(m_service.Forecast("stream-b", Seconds(150)).Next!.EventId, Is.EqualTo(2));
        });
    }

    [Test]
    public void TheTablesAreRememberedBetweenCalls()
    {
        // the PAT and PMT arrive in one batch of packets and the cue in the next
        var cue = SpliceSectionBuilder.SpliceInsert(1, ptsTime: Seconds(200), durationTicks: Seconds(60)).Build();

        m_service.Ingest("stream-a", Scte35TestStream.Tables(PmtPid, CuePid));
        m_service.Ingest("stream-a", Scte35TestStream.CuePacket(CuePid, cue));

        Assert.That(m_service.Forecast("stream-a", Seconds(150)).Next, Is.Not.Null);
    }

    [Test]
    public void AForgottenStream_ForecastsNothingAgain()
    {
        m_service.Ingest("stream-a", StreamAnnouncing(1, Seconds(200), Seconds(60)));
        m_service.Forget("stream-a");

        Assert.That(m_service.Forecast("stream-a", Seconds(150)), Is.EqualTo(AdBreakForecast.Empty));
    }

    [Test]
    public void AnImmediateCue_LandsWhereTheStreamWasLastSeen()
    {
        // an immediate splice carries no time, so the last forecast is all there is
        // to say where the stream is
        m_service.Ingest("stream-a", Scte35TestStream.Tables(PmtPid, CuePid));
        m_service.Forecast("stream-a", Seconds(1000));

        var immediate = SpliceSectionBuilder.SpliceInsert(1, ptsTime: null, durationTicks: Seconds(60)).Build();
        m_service.Ingest("stream-a", Scte35TestStream.CuePacket(CuePid, immediate));

        Assert.That(m_service.Forecast("stream-a", Seconds(1000)).InProgress, Is.Not.Null);
    }

    [Test]
    public void FeedingAndForecastingAtOnce_DoesNotFallOver()
    {
        var ingest = Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                m_service.Ingest("stream-a", StreamAnnouncing((uint)i, Seconds(200 + i), Seconds(60)));
            }
        });

        var forecast = Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                m_service.Forecast("stream-a", Seconds(150));
            }
        });

        Assert.DoesNotThrowAsync(() => Task.WhenAll(ingest, forecast));
    }
}
