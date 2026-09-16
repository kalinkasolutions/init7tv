using Init7Tv.BusinessLogic.Subtitles;

namespace Init7Tv.UnitTest.Subtitles;

/// <summary>
/// Reading the teletext descriptor, ETSI EN 300 468 6.2.43. The fixtures are
/// the bytes these channels actually carry.
/// </summary>
public class TeletextDescriptorTest
{
    /// <summary>arte D, pid 0x1ee, as carried: index 100 deu, subtitles 150 deu and 888 fra.</summary>
    private const string ArteD = "646575090064657511506672612888";

    /// <summary>SRF 1 FHD, pid 0x1fb, as carried: index 100 deu and subtitles on 777.</summary>
    private const string Srf1 = "64657509006465751777";

    private static IReadOnlyList<TeletextComponent> Parse(string hex) =>
        TeletextDescriptor.Parse(Convert.FromHexString(hex));

    [Test]
    public void ArteCarriesGermanAndFrenchOnDifferentPages()
    {
        // the case that makes pages necessary: asking for every subtitle page here
        // returns two languages interleaved in one track
        var components = Parse(ArteD);

        var subtitles = components.Where(x => x.IsSubtitle).ToArray();

        Assert.That(subtitles, Has.Length.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(subtitles[0].Language, Is.EqualTo("deu"));
            Assert.That(subtitles[0].Page, Is.EqualTo(150));
            Assert.That(subtitles[0].HearingImpaired, Is.False);
            Assert.That(subtitles[1].Language, Is.EqualTo("fra"));
            Assert.That(subtitles[1].Page, Is.EqualTo(888));
            Assert.That(subtitles[1].HearingImpaired, Is.True);
        });
    }

    [Test]
    public void TheIndexPageIsNotASubtitle()
    {
        // page 100 is the teletext service itself, football tables and all
        var components = Parse(ArteD);

        Assert.That(components[0].Page, Is.EqualTo(100));
        Assert.That(components[0].IsSubtitle, Is.False);
    }

    [Test]
    public void SrfCarriesOneSubtitlePage()
    {
        var subtitles = Parse(Srf1).Where(x => x.IsSubtitle).ToArray();

        Assert.That(subtitles, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(subtitles[0].Language, Is.EqualTo("deu"));
            Assert.That(subtitles[0].Page, Is.EqualTo(777));
        });
    }

    [TestCase(0x1, 0x00, 100)]
    [TestCase(0x7, 0x77, 777)]
    [TestCase(0x1, 0x50, 150)]
    [TestCase(0x8 & 0x7, 0x88, 888)]
    [TestCase(0x3, 0x99, 399)]
    public void MagazineAndPageMakeTheNumberAViewerKnows(int magazine, byte page, int expected)
    {
        // the page is two BCD digits, and magazine zero means magazine eight
        var descriptor = new byte[] { 0x64, 0x65, 0x75, (byte)((0x02 << 3) | magazine), page };

        Assert.That(TeletextDescriptor.Parse(descriptor)[0].Page, Is.EqualTo(expected));
    }

    [Test]
    public void AnEmptyDescriptorHasNoComponents()
    {
        Assert.That(TeletextDescriptor.Parse([]), Is.Empty);
    }

    [Test]
    public void ATrailingPartOfAComponentIsIgnored()
    {
        // each component is five bytes and a short one cannot be read
        var truncated = Convert.FromHexString(Srf1)[..8];

        Assert.That(TeletextDescriptor.Parse(truncated), Has.Count.EqualTo(1));
    }

    [Test]
    public void EveryTruncationIsReadWithoutThrowing()
    {
        var full = Convert.FromHexString(ArteD);

        for (var length = 0; length <= full.Length; length++)
        {
            Assert.DoesNotThrow(() => TeletextDescriptor.Parse(full.AsSpan(0, length)));
        }
    }

    [Test]
    public void OtherTeletextTypesAreNotSubtitles()
    {
        // 0x03 is additional information and 0x04 a programme schedule
        foreach (var type in new[] { 0x01, 0x03, 0x04 })
        {
            var descriptor = new byte[] { 0x64, 0x65, 0x75, (byte)((type << 3) | 1), 0x00 };

            Assert.That(TeletextDescriptor.Parse(descriptor)[0].IsSubtitle, Is.False, $"type {type}");
        }
    }
}
