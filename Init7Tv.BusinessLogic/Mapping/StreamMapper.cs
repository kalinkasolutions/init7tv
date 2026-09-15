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

    /// <summary>
    /// Indexes are positions among the audio streams, matching ffmpeg's <c>-map 0:a:N</c>.
    /// Tracks without a language tag keep their slot so the numbering stays aligned.
    /// </summary>
    private static LanguageDto[] ToDto(this IReadOnlyCollection<StreamInfo> streamInfos)
    {
        return streamInfos
            .Where(x => x.CodecType == "audio")
            .Select((stream, audioIndex) => new
            {
                Language = stream.Tags?.GetValueOrDefault("language"),
                Index = audioIndex
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