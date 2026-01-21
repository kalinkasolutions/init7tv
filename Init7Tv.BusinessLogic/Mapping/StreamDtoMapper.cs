using Init7Tv.BusinessLogic.Ffprobe;
using Init7Tv.Dto;

namespace Init7Tv.BusinessLogic.Mapping;

public static class StreamDtoMapper
{
    public static StreamDto Map(TvStream stream)
    {
        return new StreamDto
        {
            StreamId = stream.StreamId,
            Languages = GetLanguageDtos(stream.StreamInfo.Streams)
        };
    }
    private static LanguageDto[] GetLanguageDtos(IReadOnlyCollection<StreamInfo> streamInfos)
    {
        return streamInfos
            .Select((stream, channel) => new { stream, index = channel })
            .Where(x => x.stream.CodecType == "audio")
            .Select(x => new
            {
                Language = x.stream.Tags?.GetValueOrDefault("language"),
                Index = x.index - 1 // index 0 is video, first audio index must also be 0
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