namespace PingWatch.Api.Models.Dtos;

public class CheckResultDto
{
    public long Id { get; set; }
    public int MonitorId { get; set; }
    public int? StatusCode { get; set; }
    public long ResponseTimeMs { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CheckedAt { get; set; }
}
