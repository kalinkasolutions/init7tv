using Init7Tv.Dto;
using Init7Tv.Dto.Init7Api;

namespace Init7Tv.BusinessLogic.Mapping;

public static class EpgMapper
{
    public static EpgDto[] ToDto(this Init7Epg[] init7Epgs)
    {
        return init7Epgs.Select(epg => new EpgDto
        {
            Id = epg.Pk,
            Date = epg.Date,
            Categories = epg.Categories,
            Description = epg.Description,
            Title = epg.Title,
            SubTitle = epg.SubTitle,
            Channel = epg.Channel,
            Lower = epg.Timeslot.Lower,
            Upper = epg.Timeslot.Upper,
        }).ToArray();
    }
}