using Init7Tv.Dto;
using Init7Tv.Dto.Init7Api;

namespace Init7Tv.BusinessLogic.Mapping;

public static class EpgMapper
{
    public static Func<Init7Epg, EpgDto> Map()
    {
        return x => new EpgDto
        {
            Id = x.Pk,
            Date = x.Date,
            Categories = x.Categories,
            Description = x.Description,
            Title = x.Title,
            SubTitle = x.SubTitle,
            Channel = x.Channel,
            Lower = x.Timeslot.Lower,
            Upper = x.Timeslot.Upper,
        };
    }
}