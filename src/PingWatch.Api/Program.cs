using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using PingWatch.Api.Data;
using PingWatch.Api.Filters;
using PingWatch.Api.Jobs;
using PingWatch.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Connection String
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// 2. Database Context
builder.Services.AddDbContext<PingWatchDbContext>(options =>
{
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
    });
});

// 3. HTTP Client with Factory
var httpTimeoutSeconds = builder.Configuration.GetValue<int>("PingWatch:HttpTimeoutSeconds", 10);
builder.Services.AddHttpClient(MonitorCheckService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(httpTimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("PingWatch-Monitor/1.0 (+https://github.com/pingwatch)");
});

// 4. Domain & Background Services
builder.Services.AddScoped<IMonitorCheckService, MonitorCheckService>();
builder.Services.AddScoped<IMonitorCheckJob, MonitorCheckJob>();

// 5. Hangfire Configuration
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.FromSeconds(5),
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true,
        PrepareSchemaIfNecessary = true
    }));

builder.Services.AddHangfireServer(options =>
{
    options.ServerName = $"PingWatchServer-{Environment.MachineName}";
    options.WorkerCount = Math.Max(2, Environment.ProcessorCount / 2);
});

// 6. Controllers & OpenAPI
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// 7. Permissive CORS for decoupled Frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// 8. Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<PingWatchDbContext>();
        logger.LogInformation("Applying EF Core migrations and ensuring database schema...");
        await db.Database.MigrateAsync();
        logger.LogInformation("Database is ready.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while initializing the database.");
        throw;
    }
}

// 9. Pipeline Configuration
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowAll");

// 10. Serve decoupled Vintage Frontend (if directory exists)
var clientPath = Path.Combine(app.Environment.ContentRootPath, "..", "..", "client");
if (!Directory.Exists(clientPath))
{
    clientPath = Path.Combine(app.Environment.ContentRootPath, "client");
}

if (Directory.Exists(clientPath))
{
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = new PhysicalFileProvider(clientPath),
        RequestPath = ""
    });

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(clientPath),
        RequestPath = ""
    });
}

// 11. Hangfire Dashboard
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireDashboardAuthorizationFilter() },
    DashboardTitle = "PingWatch Dashboard"
});

// 12. Register Recurring Background Job
var cronExpression = app.Configuration.GetValue<string>("PingWatch:CheckCronExpression") ?? "*/5 * * * *";
RecurringJob.AddOrUpdate<IMonitorCheckJob>(
    recurringJobId: "pingwatch-check-all-monitors",
    methodCall: job => job.ExecuteAllMonitorsCheckAsync(),
    cronExpression: cronExpression
);

app.MapControllers();

app.Run();
