using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PingWatch.Api.Data;
using PingWatch.Api.Entities;

namespace PingWatch.Api.Services;

public class MonitorCheckService : IMonitorCheckService
{
    private readonly PingWatchDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MonitorCheckService> _logger;

    public const string HttpClientName = "PingWatchClient";

    public MonitorCheckService(
        PingWatchDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ILogger<MonitorCheckService> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CheckResult> CheckSingleMonitorAsync(int monitorId, CancellationToken cancellationToken = default)
    {
        var monitor = await _dbContext.Monitors.FindAsync([monitorId], cancellationToken)
            ?? throw new InvalidOperationException($"Monitor with Id {monitorId} was not found.");

        return await PerformCheckAndSaveAsync(monitor, cancellationToken);
    }

    public async Task<int> CheckAllActiveMonitorsAsync(CancellationToken cancellationToken = default)
    {
        var activeMonitors = await _dbContext.Monitors
            .Where(m => m.IsActive)
            .ToListAsync(cancellationToken);

        if (activeMonitors.Count == 0)
        {
            _logger.LogInformation("No active monitors found to check.");
            return 0;
        }

        _logger.LogInformation("Starting health check run for {Count} active monitors.", activeMonitors.Count);

        var processedCount = 0;
        foreach (var monitor in activeMonitors)
        {
            try
            {
                await PerformCheckAndSaveAsync(monitor, cancellationToken);
                processedCount++;
            }
            catch (Exception ex)
            {
                // Unhandled unexpected crash per site should never stop the batch loop
                _logger.LogError(ex, "Unexpected error processing monitor {MonitorId} ({Url}).", monitor.Id, monitor.Url);
            }
        }

        _logger.LogInformation("Completed health check run. Processed {ProcessedCount}/{TotalCount} monitors.",
            processedCount, activeMonitors.Count);

        return processedCount;
    }

    private async Task<CheckResult> PerformCheckAndSaveAsync(Entities.Monitor monitor, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var stopwatch = Stopwatch.StartNew();

        int? statusCode = null;
        var isSuccess = false;
        string? errorMessage = null;

        try
        {
            // Use GET request with cancellation token and ResponseHeadersRead to avoid downloading large bodies
            using var request = new HttpRequestMessage(HttpMethod.Get, monitor.Url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            stopwatch.Stop();
            statusCode = (int)response.StatusCode;
            isSuccess = response.IsSuccessStatusCode;

            if (!isSuccess)
            {
                errorMessage = $"HTTP status code indicates failure: {statusCode} {response.ReasonPhrase}";
            }
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken != cancellationToken)
        {
            stopwatch.Stop();
            isSuccess = false;
            errorMessage = "Request timed out after waiting for response.";
            _logger.LogWarning("Timeout checking monitor {MonitorId} ({Url}).", monitor.Id, monitor.Url);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            statusCode = (int?)ex.StatusCode;
            isSuccess = false;
            errorMessage = ex.InnerException != null
                ? $"{ex.Message} -> {ex.InnerException.Message}"
                : ex.Message;

            _logger.LogWarning(ex, "HTTP request failed for monitor {MonitorId} ({Url}).", monitor.Id, monitor.Url);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            isSuccess = false;
            errorMessage = $"Unexpected check error: {ex.Message}";
            _logger.LogError(ex, "General error checking monitor {MonitorId} ({Url}).", monitor.Id, monitor.Url);
        }

        var result = new CheckResult
        {
            MonitorId = monitor.Id,
            StatusCode = statusCode,
            ResponseTimeMs = stopwatch.ElapsedMilliseconds,
            IsSuccess = isSuccess,
            ErrorMessage = errorMessage,
            CheckedAt = DateTime.UtcNow
        };

        _dbContext.CheckResults.Add(result);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Checked monitor {MonitorId} ({Url}): {Status} ({Elapsed}ms)",
            monitor.Id, monitor.Url, isSuccess ? "UP" : "DOWN", stopwatch.ElapsedMilliseconds);

        return result;
    }
}
