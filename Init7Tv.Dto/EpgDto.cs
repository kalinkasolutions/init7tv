namespace Init7Tv.Dto;

public sealed class EpgDto
{
    public Guid Id { get; set; }
    public DateTime Lower { get; set; }
    public DateTime Upper { get; set; }
    public Guid Channel { get; set; }
    public string Title { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public string[] Categories { get; set; } = [];
    public DateTime? Date { get; set; }
    public string SubTitle { get; set; }
}