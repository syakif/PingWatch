using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PingWatch.Api.Data;
using PingWatch.Api.Entities;
using PingWatch.Api.Models.Dtos;
using PingWatch.Api.Services;

namespace PingWatch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MonitorsController : ControllerBase
{
    private readonly PingWatchDbContext _dbContext;
    private readonly IMonitorCheckService _checkService;
    private readonly ILogger<MonitorsController> _logger;

    public MonitorsController(
        PingWatchDbContext dbContext,
        IMonitorCheckService checkService,
        ILogger<MonitorsController> logger)
    {
        _dbContext = dbContext;
        _checkService = checkService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all monitors with their latest health check status snapshot.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<MonitorResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<MonitorResponseDto>>> GetAll(CancellationToken cancellationToken)
    {
        var monitors = await _dbContext.Monitors
            .AsNoTracking()
            .Select(m => new
            {
                Monitor = m,
                LatestCheck = m.CheckResults
                    .OrderByDescending(cr => cr.CheckedAt)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var response = monitors.Select(item => new MonitorResponseDto
        {
            Id = item.Monitor.Id,
            Name = item.Monitor.Name,
            Url = item.Monitor.Url,
            IsActive = item.Monitor.IsActive,
            CreatedAt = item.Monitor.CreatedAt,
            LastCheckedAt = item.LatestCheck?.CheckedAt,
            LastStatusCode = item.LatestCheck?.StatusCode,
            LastResponseTimeMs = item.LatestCheck?.ResponseTimeMs,
            IsUp = item.LatestCheck?.IsSuccess,
            LastErrorMessage = item.LatestCheck?.ErrorMessage
        }).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Creates a new website monitor to be periodically checked.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(MonitorResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MonitorResponseDto>> Create(
        [FromBody] CreateMonitorDto request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Validate URL scheme
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var validatedUri) ||
            (validatedUri.Scheme != Uri.UriSchemeHttp && validatedUri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest(new { message = "Only valid HTTP and HTTPS URLs are supported." });
        }

        var monitor = new Entities.Monitor
        {
            Name = request.Name.Trim(),
            Url = validatedUri.ToString(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Monitors.Add(monitor);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Added new monitor {MonitorId} for URL {Url}", monitor.Id, monitor.Url);

        // Enqueue an immediate one-off background check via Hangfire
        BackgroundJob.Enqueue<IMonitorCheckService>(service => 
            service.CheckSingleMonitorAsync(monitor.Id, CancellationToken.None));

        var responseDto = new MonitorResponseDto
        {
            Id = monitor.Id,
            Name = monitor.Name,
            Url = monitor.Url,
            IsActive = monitor.IsActive,
            CreatedAt = monitor.CreatedAt
        };

        return CreatedAtAction(nameof(GetChecksHistory), new { id = monitor.Id }, responseDto);
    }

    /// <summary>
    /// Gets check history for a specific monitor.
    /// </summary>
    [HttpGet("{id:int}/checks")]
    [ProducesResponseType(typeof(IEnumerable<CheckResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<CheckResultDto>>> GetChecksHistory(
        int id,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var monitorExists = await _dbContext.Monitors.AnyAsync(m => m.Id == id, cancellationToken);
        if (!monitorExists)
        {
            return NotFound(new { message = $"Monitor with ID {id} was not found." });
        }

        // Clamp limit between 1 and 100
        limit = Math.Clamp(limit, 1, 100);

        var history = await _dbContext.CheckResults
            .AsNoTracking()
            .Where(cr => cr.MonitorId == id)
            .OrderByDescending(cr => cr.CheckedAt)
            .Take(limit)
            .Select(cr => new CheckResultDto
            {
                Id = cr.Id,
                MonitorId = cr.MonitorId,
                StatusCode = cr.StatusCode,
                ResponseTimeMs = cr.ResponseTimeMs,
                IsSuccess = cr.IsSuccess,
                ErrorMessage = cr.ErrorMessage,
                CheckedAt = cr.CheckedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(history);
    }

    /// <summary>
    /// Manually triggers an on-demand check for a specific monitor.
    /// </summary>
    [HttpPost("{id:int}/check")]
    [ProducesResponseType(typeof(CheckResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CheckResultDto>> TriggerCheck(int id, CancellationToken cancellationToken)
    {
        var monitorExists = await _dbContext.Monitors.AnyAsync(m => m.Id == id, cancellationToken);
        if (!monitorExists)
        {
            return NotFound(new { message = $"Monitor with ID {id} was not found." });
        }

        var result = await _checkService.CheckSingleMonitorAsync(id, cancellationToken);

        return Ok(new CheckResultDto
        {
            Id = result.Id,
            MonitorId = result.MonitorId,
            StatusCode = result.StatusCode,
            ResponseTimeMs = result.ResponseTimeMs,
            IsSuccess = result.IsSuccess,
            ErrorMessage = result.ErrorMessage,
            CheckedAt = result.CheckedAt
        });
    }

    /// <summary>
    /// Deletes a monitor and all its recorded check history.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var monitor = await _dbContext.Monitors.FindAsync([id], cancellationToken);
        if (monitor == null)
        {
            return NotFound(new { message = $"Monitor with ID {id} was not found." });
        }

        _dbContext.Monitors.Remove(monitor);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted monitor {MonitorId} ({Name})", id, monitor.Name);

        return NoContent();
    }
}
