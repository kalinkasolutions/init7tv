using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic.Mapping;

public static class StreamMapper
{
    public static StreamDto ToDto(this TvStream stream)
    {
        return new StreamDto
        {
            StreamId = stream.StreamId,
            Languages = stream.StreamInfo.Streams.ToDto()
        };
    }

    private static LanguageDto[] ToDto(this IReadOnlyCollection<StreamInfo> streamInfos)
    {
        return streamInfos
            .Select((stream, channel) => new { stream, index = channel })
            .Where(x => x.stream.CodecType == "audio")
            .Select(x => new
            {
                Language = x.stream.Tags?.GetValueOrDefault("language"),
                Index = x.index - 1
            })
            .Where(x => x.Language != null)
            .Select(x => new LanguageDto
            {
                Language = x.Language!,
                AudioStreamIndex = x.Index
            })
            .ToArray();
    }
}