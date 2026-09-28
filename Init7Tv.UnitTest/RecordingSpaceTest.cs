using Init7Tv.BusinessLogic.Recording;

namespace Init7Tv.UnitTest;

public class RecordingSpaceTest
{
    private const long Floor = 5L * 1024 * 1024 * 1024;

    [Test]
    public void ALengthIsSizedOnTopOfTheFloor()
    {
        var needed = RecordingSpace.Needed(TimeSpan.FromMinutes(30), Floor);

        Assert.That(needed, Is.EqualTo(1800L * RecordingSpace.BytesPerSecondEstimate + Floor));
    }

    /// <summary>
    /// A recording with no end is sized against its backstop only if it is sized at all, and the
    /// backstop is a day: that would ask for over a hundred gigabytes and refuse on any real disk.
    /// The floor is the whole of what can honestly be asked before it starts.
    /// </summary>
    [Test]
    public void NoLengthAsksOnlyForTheFloor()
    {
        Assert.That(RecordingSpace.Needed(null, Floor), Is.EqualTo(Floor));
    }

    [Test]
    public void ALengthOfNothingAsksOnlyForTheFloorToo()
    {
        Assert.That(RecordingSpace.Needed(TimeSpan.Zero, Floor), Is.EqualTo(Floor));
    }

    [Test]
    public void RoomEnoughIsNothingInTheWay()
    {
        Assert.That(RecordingSpace.TooLittle(Floor * 2, Floor), Is.Null);
    }

    [Test]
    public void TooLittleSaysHowMuchIsLeft()
    {
        // the page shows this rather than saying nothing
        var message = RecordingSpace.TooLittle(2_000_000_000, Floor);

        Assert.That(message, Does.Contain("2.0 GB"));
    }

    [Test]
    public void ACaptureCarriesOnWhileTheFloorIsClear()
    {
        Assert.That(RecordingSpace.RunningOut(Floor + 1, Floor), Is.Null);
    }

    [Test]
    public void ACaptureStopsOnceTheFloorIsReached()
    {
        Assert.That(RecordingSpace.RunningOut(Floor - 1, Floor), Is.Not.Null);
    }

    [Test]
    public void StoppingSaysWhy()
    {
        var reason = RecordingSpace.RunningOut(1_500_000_000, Floor);

        Assert.That(reason, Does.Contain("1.5 GB"));
    }
}
