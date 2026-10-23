namespace PingWatch.Api.Models.Dtos;

public class MonitorResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    // Latest check snapshot
    public DateTime? LastCheckedAt { get; set; }
    public int? LastStatusCode { get; set; }
    public long? LastResponseTimeMs { get; set; }
    public bool? IsUp { get; set; }
    public string? LastErrorMessage { get; set; }
}
