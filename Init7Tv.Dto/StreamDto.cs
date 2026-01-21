namespace Init7Tv.Dto;

public class StreamDto
{
    public string StreamId { get; set; } = string.Empty;
    public IReadOnlyCollection<LanguageDto> Languages { get; set; }
}