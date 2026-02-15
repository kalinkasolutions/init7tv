using Init7Tv.Dto.Init7Api;

namespace Init7Tv.BusinessLogic.Init7Api;

public static class FullHdSrg
{
    public static readonly Init7TvChannel[] FullHdChannels =
    [
        new()
        {
            ManuallyAdded = true,
            CanonicalName = "SRF1.ch",
            Pk = new Guid("ed7d7676-9b05-419a-99b4-5f993d238f80"),
            Logo = "https://vtvapi03.sys.init7.net/media/logos/1102_SRF1.ch.png",
            Name = "SRF 1 FHD",
            HlsSrc = "https://vtvapi03.sys.init7.net/api/live/?channel=ed7d7676-9b05-419a-99b4-5f993d238f80"
        },
        new()
        {
            ManuallyAdded = true,
            CanonicalName = "SRFzwei.ch",
            Pk = new Guid("84b6cf07-7fb9-44df-9769-4ecc47100497"),
            Logo = "https://vtvapi03.sys.init7.net/media/logos/1104_SRFzwei.ch.png",
            Name = "SRF zwei FHD",
            HlsSrc = "https://vtvapi03.sys.init7.net/api/live/?channel=84b6cf07-7fb9-44df-9769-4ecc47100497"
        },
        new()
        {
            ManuallyAdded = true,
            CanonicalName = "SRFinfo.ch",
            Pk = new Guid("c4fdba91-54a7-4b6e-876e-e16d8f90e375"),
            Logo = "https://vtvapi03.sys.init7.net/media/logos/1106_SRFinfo.ch.png",
            Name = "SRF info FHD",
            HlsSrc = "https://vtvapi03.sys.init7.net/api/live/?channel=c4fdba91-54a7-4b6e-876e-e16d8f90e375"
        },
        new()
        {
            ManuallyAdded = true,
            CanonicalName = "RTS1.ch",
            Pk = new Guid("be382348-42aa-488a-a73c-f8ef1f410872"),
            Logo = "https://vtvapi03.sys.init7.net/media/logos/2103_RTS1.ch.png",
            Name = "RTS 1 FHD",
            HlsSrc = "https://vtvapi03.sys.init7.net/api/live/?channel=be382348-42aa-488a-a73c-f8ef1f410872"
        },
        new()
        {
            ManuallyAdded = true,
            CanonicalName = "RTS2.ch",
            Pk = new Guid("c6786c12-83b5-4183-b705-61db667f829a"),
            Logo = "https://vtvapi03.sys.init7.net/media/logos/2106_RTS2.ch.png",
            Name = "RTS 2 FHD",
            HlsSrc = "https://vtvapi03.sys.init7.net/api/live/?channel=c6786c12-83b5-4183-b705-61db667f829a"
        },
        new()
        {
            ManuallyAdded = true,
            CanonicalName = "RSILa1.ch",
            Pk = new Guid("a2776d37-e7c5-4be2-8801-ac500e0917fd"),
            Logo = "https://vtvapi03.sys.init7.net/media/logos/3104_RSILa1.ch.png",
            Name = "RSI LA 1 FHD",
            HlsSrc = "https://vtvapi03.sys.init7.net/api/live/?channel=a2776d37-e7c5-4be2-8801-ac500e0917fd"
        },
        new()
        {
            ManuallyAdded = true,
            CanonicalName = "RSILa2.ch",
            Pk = new Guid("eb0538fc-049a-4447-9d40-46809c5c54ef"),
            Logo = "https://vtvapi03.sys.init7.net/media/logos/3108_RSILa2.ch.png",
            Name = "RSI LA 2 FHD",
            HlsSrc = "https://vtvapi03.sys.init7.net/api/live/?channel=eb0538fc-049a-4447-9d40-46809c5c54ef"
        }
    ];
}