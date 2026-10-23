using Hangfire;
using PingWatch.Api.Services;

namespace PingWatch.Api.Jobs;

public class MonitorCheckJob : IMonitorCheckJob
{
    private readonly IMonitorCheckService _checkService;
    private readonly ILogger<MonitorCheckJob> _logger;

    public MonitorCheckJob(IMonitorCheckService checkService, ILogger<MonitorCheckJob> logger)
    {
        _checkService = checkService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAllMonitorsCheckAsync()
    {
        _logger.LogInformation("Hangfire recurring job [check-all-monitors] triggered at {Time}", DateTime.UtcNow);

        var count = await _checkService.CheckAllActiveMonitorsAsync();

        _logger.LogInformation("Hangfire recurring job [check-all-monitors] completed successfully. Inspected {Count} monitors.", count);
    }
}
