using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Binah.Pipeline.Controllers
{
    /// <summary>
    /// Health check endpoints for Kubernetes liveness and readiness probes
    /// Implements comprehensive health checks for ETL pipeline service
    /// </summary>
    [ApiController]
    [Route("health")]
    [AllowAnonymous]  // Health checks should not require authentication
    public class HealthController : ControllerBase
    {
        private readonly HealthCheckService _healthCheckService;
        private readonly ILogger<HealthController> _logger;

        public HealthController(
            HealthCheckService healthCheckService,
            ILogger<HealthController> logger)
        {
            _healthCheckService = healthCheckService;
            _logger = logger;
        }

        /// <summary>
        /// Overall health status with all dependency checks
        /// Returns 200 OK when healthy or degraded, 503 Service Unavailable when unhealthy
        /// Checks: PostgreSQL, Hangfire (storage + servers), pipeline queue, Kafka, active pipelines, connectors
        /// Note: Degraded state (e.g., Kafka unavailable) returns 200 to allow service operation
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            try
            {
                var report = await _healthCheckService.CheckHealthAsync();

                var response = new
                {
                    status = report.Status.ToString(),
                    service = "binah-pipeline",
                    checks = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description,
                        duration = e.Value.Duration.TotalMilliseconds,
                        exception = e.Value.Exception?.Message,
                        tags = e.Value.Tags
                    }),
                    totalDuration = report.TotalDuration.TotalMilliseconds,
                    timestamp = DateTime.UtcNow
                };

                // Return 200 for Healthy or Degraded, 503 only for Unhealthy
                // Degraded state allows service to operate (e.g., Kafka down but ETL still works)
                var statusCode = report.Status == HealthStatus.Unhealthy ? 503 : 200;

                if (report.Status == HealthStatus.Degraded)
                {
                    _logger.LogWarning("Health check degraded: {DegradedChecks}",
                        string.Join(", ", report.Entries
                            .Where(e => e.Value.Status == HealthStatus.Degraded)
                            .Select(e => $"{e.Key}: {e.Value.Description}")));
                }
                else if (report.Status == HealthStatus.Unhealthy)
                {
                    _logger.LogError("Health check failed: {UnhealthyChecks}",
                        string.Join(", ", report.Entries
                            .Where(e => e.Value.Status == HealthStatus.Unhealthy)
                            .Select(e => $"{e.Key}: {e.Value.Description}")));
                }

                return StatusCode(statusCode, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check endpoint failed");
                return StatusCode(503, new
                {
                    status = "unhealthy",
                    service = "binah-pipeline",
                    error = "Health check execution failed",
                    exception = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Readiness probe - indicates if the service can accept traffic
        /// Used by Kubernetes to determine if pod should receive requests
        /// Critical checks: PostgreSQL (db tag), Hangfire storage
        /// Non-critical: Kafka (service can operate in degraded mode), connector count
        /// </summary>
        [HttpGet("ready")]
        public async Task<IActionResult> Ready()
        {
            try
            {
                var report = await _healthCheckService.CheckHealthAsync();

                // Service is ready if critical dependencies are healthy
                // Critical: PostgreSQL (tagged 'db'), Hangfire storage
                // Non-critical: Kafka, active pipelines, connector count
                var criticalChecks = report.Entries
                    .Where(e => e.Value.Tags.Contains("db") || e.Key == "hangfire-storage")
                    .ToList();

                var isCriticalHealthy = criticalChecks.All(e => e.Value.Status == HealthStatus.Healthy);

                // Service is ready if critical checks are healthy, even if overall status is Degraded
                if (isCriticalHealthy)
                {
                    return Ok(new
                    {
                        status = "ready",
                        service = "binah-pipeline",
                        overallStatus = report.Status.ToString(),
                        criticalChecks = criticalChecks.Select(e => new
                        {
                            name = e.Key,
                            status = e.Value.Status.ToString()
                        }),
                        timestamp = DateTime.UtcNow
                    });
                }

                _logger.LogWarning("Readiness check failed: Critical dependencies unhealthy");
                return StatusCode(503, new
                {
                    status = "not_ready",
                    service = "binah-pipeline",
                    failedCriticalChecks = criticalChecks
                        .Where(e => e.Value.Status != HealthStatus.Healthy)
                        .Select(e => new
                        {
                            name = e.Key,
                            status = e.Value.Status.ToString(),
                            description = e.Value.Description
                        }),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Readiness check failed");
                return StatusCode(503, new
                {
                    status = "not_ready",
                    service = "binah-pipeline",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Liveness probe - indicates if the process is running
        /// Used by Kubernetes to determine if pod should be restarted
        /// Should always return 200 OK unless the process is completely dead
        /// Does not check dependencies - only process health
        /// </summary>
        [HttpGet("live")]
        public IActionResult Live()
        {
            return Ok(new
            {
                status = "alive",
                service = "binah-pipeline",
                port = 8094,
                version = "1.0.0",
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
                timestamp = DateTime.UtcNow
            });
        }
    }
}
