using Binah.Pipeline.Data;
using Binah.Pipeline.Orchestration;
using Binah.Pipeline.Repositories;
using Binah.Pipeline.Transformations;
using Binah.Pipeline.Hubs;
using Binah.Pipeline.Connectors;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Confluent.Kafka;
using System.Reflection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Binah.Core.Middleware;
using Serilog;
using Prometheus;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json")
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .AddEnvironmentVariables()
        .Build())
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "binah-pipeline")
    .WriteTo.Console()
    .WriteTo.File("logs/binah-pipeline-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for logging
builder.Host.UseSerilog();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null; // Keep property names as-is
        options.JsonSerializerOptions.WriteIndented = true;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Binah Pipeline API", Version = "v1" });
});

// Add SignalR for real-time pipeline updates
builder.Services.AddSignalR();

// Add PostgreSQL with Entity Framework Core
builder.Services.AddDbContext<PipelineDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions => npgsqlOptions.MigrationsAssembly("Binah.Pipeline")
    ));

// Add Hangfire for scheduling
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(builder.Configuration.GetConnectionString("Hangfire")));

builder.Services.AddHangfireServer();

// Register repositories
builder.Services.AddScoped<IPipelineRepository, PipelineRepository>();
builder.Services.AddScoped<Binah.Pipeline.Repositories.IConnectionRepository, Binah.Pipeline.Repositories.ConnectionRepository>();

// Register connection service
builder.Services.AddScoped<Binah.Pipeline.Services.IConnectionService, Binah.Pipeline.Services.ConnectionService>();

// Register transformation services
builder.Services.AddScoped<JoinTransformation>();
builder.Services.AddScoped<MergeTransformation>();

// Register orchestration services
builder.Services.AddScoped<NodeGraphExecutor>();
builder.Services.AddScoped<PipelineOrchestrator>();

// Register scheduler service
builder.Services.AddScoped<Binah.Pipeline.Services.PipelineSchedulerService>();

// Register schedule loader hosted service
builder.Services.AddHostedService<Binah.Pipeline.Services.ScheduleLoaderService>();

// Register write-back services
builder.Services.AddScoped<Binah.Pipeline.WriteBack.IWriteBackConnector, Binah.Pipeline.WriteBack.Connectors.SalesforceWriteBackConnector>();
builder.Services.AddScoped<Binah.Pipeline.WriteBack.IWriteBackConnector, Binah.Pipeline.WriteBack.Connectors.SAPWriteBackConnector>();
builder.Services.AddScoped<Binah.Pipeline.WriteBack.Audit.IWriteBackAuditLogger, Binah.Pipeline.WriteBack.Audit.WriteBackAuditLogger>();
builder.Services.AddScoped<Binah.Pipeline.WriteBack.ApprovalWorkflow.IWriteBackApprovalService, Binah.Pipeline.WriteBack.ApprovalWorkflow.WriteBackApprovalService>();
builder.Services.AddScoped<Binah.Pipeline.WriteBack.ApprovalWorkflow.IWriteBackManager, Binah.Pipeline.WriteBack.WriteBackManager>();

// Register write-back Kafka consumer as hosted service
builder.Services.AddHostedService<Binah.Pipeline.Consumers.EntityUpdatedConsumer>();

// Register Kafka event publisher as singleton (shared producer for all pipeline executions)
builder.Services.AddSingleton<Binah.Pipeline.Events.IEventPublisher, Binah.Pipeline.Events.EventPublisher>();

// Configure Health Checks
builder.Services.AddHealthChecks()
    // PostgreSQL connection check
    .AddCheck("postgresql", () =>
    {
        try
        {
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection string is not configured");
            using var conn = new Npgsql.NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.ExecuteScalar();
            return HealthCheckResult.Healthy("PostgreSQL is accessible");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"PostgreSQL unreachable: {ex.Message}");
        }
    }, tags: new[] { "db", "sql" })
    // Hangfire storage check
    .AddCheck("hangfire-storage", () =>
    {
        try
        {
            var monitoringApi = JobStorage.Current.GetMonitoringApi();
            var stats = monitoringApi.GetStatistics();
            return HealthCheckResult.Healthy($"Enqueued: {stats.Enqueued}, Servers: {stats.Servers}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Hangfire storage error: {ex.Message}");
        }
    })
    // Hangfire servers check
    .AddCheck("hangfire-servers", () =>
    {
        try
        {
            var monitoringApi = JobStorage.Current.GetMonitoringApi();
            var servers = monitoringApi.Servers();

            return servers.Count == 0
                ? HealthCheckResult.Degraded("No Hangfire servers running")
                : HealthCheckResult.Healthy($"{servers.Count} server(s) active");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Cannot check Hangfire servers: {ex.Message}");
        }
    })
    // Pipeline execution queue check
    .AddCheck("pipeline-execution-queue", () =>
    {
        try
        {
            using var scope = builder.Services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();

            var queuedCount = db.PipelineExecutions
                .Where(p => p.Status == "pending" || p.Status == "running")
                .Count();

            return queuedCount > 100
                ? HealthCheckResult.Degraded($"High queue: {queuedCount} executions")
                : HealthCheckResult.Healthy($"Queue: {queuedCount}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Cannot check execution queue: {ex.Message}");
        }
    })
    // Kafka connection check
    .AddCheck("kafka-connection", () =>
    {
        try
        {
            var kafkaBootstrapServers = builder.Configuration["Kafka:BootstrapServers"];
            if (string.IsNullOrEmpty(kafkaBootstrapServers))
            {
                return HealthCheckResult.Degraded("Kafka not configured");
            }

            var config = new AdminClientConfig
            {
                BootstrapServers = kafkaBootstrapServers
            };
            using var adminClient = new AdminClientBuilder(config).Build();
            // Quick metadata request to verify connectivity
            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));
            return HealthCheckResult.Healthy($"Connected to {metadata.Brokers.Count} broker(s)");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Kafka unavailable: {ex.Message}");
        }
    })
    // Active pipelines check
    .AddCheck("active-pipelines", () =>
    {
        try
        {
            using var scope = builder.Services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();

            var activePipelines = db.PipelineSchedules
                .Where(p => p.Enabled)
                .Count();

            return HealthCheckResult.Healthy($"{activePipelines} scheduled pipeline(s) active");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Cannot check active pipelines: {ex.Message}");
        }
    })
    // Connector registry check
    .AddCheck("connector-registry", () =>
    {
        try
        {
            // Count all types implementing IConnector interface
            var connectorTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => typeof(IConnector).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                .ToList();

            var connectorCount = connectorTypes.Count;

            return connectorCount >= 14
                ? HealthCheckResult.Healthy($"{connectorCount} connector(s) registered")
                : HealthCheckResult.Degraded($"Only {connectorCount} connector(s) available (expected 14)");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Cannot check connector registry: {ex.Message}");
        }
    });

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Authentication:Jwt");
var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT Secret Key is not configured");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

// Apply migrations on startup (development only - use proper migration strategy in production)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
    try
    {
        await dbContext.Database.MigrateAsync();
        app.Logger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error applying database migrations");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Use custom middleware - FIRST before authentication
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors("AllowAll");
app.UseHttpsRedirection();

// CRITICAL: Authentication must come before Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Hangfire dashboard
app.UseHangfireDashboard();

// SignalR hubs
app.MapHub<PipelineHub>("/hubs/pipeline");

// Health check endpoints handled by HealthController:
// - GET /health - Overall health with all checks
// - GET /health/ready - Readiness probe (critical dependencies only)
// - GET /health/live - Liveness probe (process health only)

// Prometheus metrics
app.UseMetricServer();
app.UseHttpMetrics();

app.Run();
