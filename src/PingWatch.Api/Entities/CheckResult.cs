namespace PingWatch.Api.Entities;

public class CheckResult
{
    public long Id { get; set; }

    public int MonitorId { get; set; }

    public Monitor Monitor { get; set; } = null!;

    public int? StatusCode { get; set; }

    public long ResponseTimeMs { get; set; }

    public bool IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
