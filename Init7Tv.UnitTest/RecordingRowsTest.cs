using Init7Tv.BusinessLogic.Recording;
using Init7Tv.Dal.Entities;
using RecordingRow = Init7Tv.Dal.Entities.Recording;

namespace Init7Tv.UnitTest;

public class RecordingRowsTest
{
    private static readonly DateTime Now = new(2026, 9, 20, 20, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ProgrammeId = Guid.NewGuid();
    private const string Directory = "/recordings/abc";

    private static PlannedRecording Pick(string userName) => new()
    {
        ProgrammeId = ProgrammeId,
        UserName = userName,
        ChannelId = Guid.NewGuid(),
        ChannelName = "SRF 1",
        CanonicalName = "srf-1",
        Title = "Tagesschau",
        SubTitle = "Die Nachrichten",
        StartsAt = Now,
        EndsAt = Now.AddHours(1)
    };

    private static RecordingRow[] New(params string[] userNames) =>
        RecordingRows.New(userNames.Select(Pick), Directory, Now.AddMinutes(-5), Now.AddHours(1), Now);

    [Test]
    public void EverybodyWhoAskedForItGetsARow()
    {
        var rows = New("niggi", "sami");

        Assert.That(rows.Select(x => x.UserName), Is.EqualTo(new[] { "niggi", "sami" }));
    }

    [Test]
    public void TheyAllShareOneCapture()
    {
        // recording the same programme twice is the one thing sharing exists to avoid
        var rows = New("niggi", "sami");

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(x => x.Directory), Is.All.EqualTo(Directory));
            Assert.That(rows.Select(x => x.RecordingId).Distinct().Count(), Is.EqualTo(2),
                "but each of them has their own row to stop and delete");
        });
    }

    [Test]
    public void ThePaddedWindowIsWhatIsStored()
    {
        // the row is what the capture is run against, so it carries the padding
        // rather than the times the guide gave
        var rows = New("niggi");

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].ScheduledStart, Is.EqualTo(Now.AddMinutes(-5)));
            Assert.That(rows[0].ScheduledEnd, Is.EqualTo(Now.AddHours(1)));
        });
    }

    [Test]
    public void ANewRowIsPendingAndHasNotStarted()
    {
        var rows = New("niggi");

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].State, Is.EqualTo(RecordingState.Pending));
            Assert.That(rows[0].StartedAt, Is.Null);
            Assert.That(rows[0].CreatedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public void WhatTheGuideSaidIsCarriedOntoTheRow()
    {
        var rows = New("niggi");

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].ProgrammeId, Is.EqualTo(ProgrammeId));
            Assert.That(rows[0].Title, Is.EqualTo("Tagesschau"));
            Assert.That(rows[0].SubTitle, Is.EqualTo("Die Nachrichten"));
            Assert.That(rows[0].ChannelName, Is.EqualTo("SRF 1"));
            Assert.That(rows[0].CanonicalName, Is.EqualTo("srf-1"));
        });
    }

    [Test]
    public void SomebodyPickingItLateJoinsTheCaptureAlreadyRunning()
    {
        var running = New("niggi");
        running[0].State = RecordingState.Recording;
        running[0].StartedAt = Now.AddMinutes(-3);

        var joining = RecordingRows.Joining([Pick("niggi"), Pick("sami")], running, Now.AddMinutes(10));

        Assert.That(joining, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(joining[0].UserName, Is.EqualTo("sami"));

            // the same window, the same directory and the same state: it is one
            // capture, and what they get is whatever it ends up holding
            Assert.That(joining[0].Directory, Is.EqualTo(running[0].Directory));
            Assert.That(joining[0].State, Is.EqualTo(RecordingState.Recording));
            Assert.That(joining[0].StartedAt, Is.EqualTo(running[0].StartedAt));
            Assert.That(joining[0].ScheduledStart, Is.EqualTo(running[0].ScheduledStart));
            Assert.That(joining[0].ScheduledEnd, Is.EqualTo(running[0].ScheduledEnd));
        });
    }

    [Test]
    public void SomebodyWhoAlreadyHasARowDoesNotGetASecond()
    {
        var running = New("niggi");

        Assert.That(RecordingRows.Joining([Pick("niggi")], running, Now), Is.Empty);
    }

    [Test]
    public void AJoinerIsNotedAtTheMomentTheyJoined()
    {
        // the row is new even though the capture is not
        var running = New("niggi");
        var joined = Now.AddMinutes(10);

        var joining = RecordingRows.Joining([Pick("sami")], running, joined);

        Assert.That(joining[0].CreatedAt, Is.EqualTo(joined));
    }
}
