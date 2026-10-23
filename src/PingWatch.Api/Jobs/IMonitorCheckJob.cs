namespace PingWatch.Api.Jobs;

public interface IMonitorCheckJob
{
    Task ExecuteAllMonitorsCheckAsync();
}
