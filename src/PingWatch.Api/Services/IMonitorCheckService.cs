using PingWatch.Api.Entities;

namespace PingWatch.Api.Services;

public interface IMonitorCheckService
{
    Task<CheckResult> CheckSingleMonitorAsync(int monitorId, CancellationToken cancellationToken = default);
    Task<int> CheckAllActiveMonitorsAsync(CancellationToken cancellationToken = default);
}
