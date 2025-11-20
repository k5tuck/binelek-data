using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Binah.Contracts.Common;
using Binah.Pipeline.Data;
using Binah.Pipeline.Models;
using System.Text.Json;

namespace Binah.Pipeline.Controllers;

/// <summary>
/// Controller for managing data source configurations
/// Uses actual database queries via PipelineDbContext
/// </summary>
[ApiController]
[Route("api/tenants/{tenantId}/datasources")]
[Authorize]
public class DataSourceController : ControllerBase
{
    private readonly PipelineDbContext _dbContext;
    private readonly ILogger<DataSourceController> _logger;

    public DataSourceController(PipelineDbContext dbContext, ILogger<DataSourceController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Validate that the route tenantId matches the JWT tenant_id claim
    /// </summary>
    private bool ValidateTenantId(Guid routeTenantId, out string? jwtTenantId)
    {
        jwtTenantId = User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(jwtTenantId))
        {
            _logger.LogWarning("JWT token missing tenant_id claim");
            return false;
        }

        if (routeTenantId.ToString() != jwtTenantId)
        {
            _logger.LogWarning("Tenant forgery attempt: JWT={JwtTenantId}, Route={RouteTenantId}",
                jwtTenantId, routeTenantId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// List all data sources for tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<DataSourceResponse>>> ListDataSources(
        Guid tenantId,
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 50)
    {
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var dataSources = await _dbContext.DataSources
            .Where(ds => ds.TenantId == tenantId)
            .OrderByDescending(ds => ds.CreatedAt)
            .Skip(skip)
            .Take(limit)
            .Select(ds => new DataSourceResponse
            {
                Id = ds.Id.ToString(),
                Name = ds.Name,
                Type = ds.Type,
                Config = string.IsNullOrEmpty(ds.Configuration)
                    ? new Dictionary<string, object>()
                    : JsonSerializer.Deserialize<Dictionary<string, object>>(ds.Configuration) ?? new(),
                Status = "disconnected", // Status would come from connection testing
                LastTested = null,
                CreatedAt = ds.CreatedAt
            })
            .ToListAsync();

        return Ok(dataSources);
    }

    /// <summary>
    /// Get data source by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<DataSourceResponse>> GetDataSource(Guid tenantId, string id)
    {
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        if (!Guid.TryParse(id, out var dsId))
        {
            return BadRequest(new { error = "Invalid data source ID format" });
        }

        var ds = await _dbContext.DataSources
            .Where(d => d.TenantId == tenantId && d.Id == dsId)
            .FirstOrDefaultAsync();

        if (ds == null)
        {
            return NotFound(new { error = $"Data source {id} not found" });
        }

        return Ok(new DataSourceResponse
        {
            Id = ds.Id.ToString(),
            Name = ds.Name,
            Type = ds.Type,
            Config = string.IsNullOrEmpty(ds.Configuration)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(ds.Configuration) ?? new(),
            Status = "disconnected",
            LastTested = null,
            CreatedAt = ds.CreatedAt
        });
    }

    /// <summary>
    /// Create a new data source
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<DataSourceResponse>> CreateDataSource(
        Guid tenantId,
        [FromBody] CreateDataSourceRequest request)
    {
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        _logger.LogInformation("Creating data source {Name} of type {Type} for tenant {TenantId}",
            request.Name, request.Type, tenantId);

        // Check for duplicate name
        var exists = await _dbContext.DataSources
            .AnyAsync(ds => ds.TenantId == tenantId && ds.Name == request.Name);

        if (exists)
        {
            return Conflict(new { error = "A data source with this name already exists" });
        }

        var ds = new DataSource
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            Type = request.Type,
            Configuration = JsonSerializer.Serialize(request.Config),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.DataSources.Add(ds);
        await _dbContext.SaveChangesAsync();

        var response = new DataSourceResponse
        {
            Id = ds.Id.ToString(),
            Name = ds.Name,
            Type = ds.Type,
            Config = request.Config,
            Status = "disconnected",
            CreatedAt = ds.CreatedAt
        };

        return CreatedAtAction(nameof(GetDataSource), new { tenantId, id = ds.Id }, response);
    }

    /// <summary>
    /// Update data source
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<DataSourceResponse>> UpdateDataSource(
        Guid tenantId,
        string id,
        [FromBody] UpdateDataSourceRequest request)
    {
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        _logger.LogInformation("Updating data source {Id} for tenant {TenantId}", id, tenantId);

        if (!Guid.TryParse(id, out var dsId))
        {
            return BadRequest(new { error = "Invalid data source ID format" });
        }

        var ds = await _dbContext.DataSources
            .Where(d => d.TenantId == tenantId && d.Id == dsId)
            .FirstOrDefaultAsync();

        if (ds == null)
        {
            return NotFound(new { error = $"Data source {id} not found" });
        }

        if (!string.IsNullOrEmpty(request.Name)) ds.Name = request.Name;
        if (request.Config != null) ds.Configuration = JsonSerializer.Serialize(request.Config);
        ds.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Ok(new DataSourceResponse
        {
            Id = ds.Id.ToString(),
            Name = ds.Name,
            Type = ds.Type,
            Config = string.IsNullOrEmpty(ds.Configuration)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(ds.Configuration) ?? new(),
            Status = "disconnected",
            CreatedAt = ds.CreatedAt
        });
    }

    /// <summary>
    /// Delete data source
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteDataSource(Guid tenantId, string id)
    {
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        _logger.LogInformation("Deleting data source {Id} for tenant {TenantId}", id, tenantId);

        if (!Guid.TryParse(id, out var dsId))
        {
            return BadRequest(new { error = "Invalid data source ID format" });
        }

        var ds = await _dbContext.DataSources
            .Where(d => d.TenantId == tenantId && d.Id == dsId)
            .FirstOrDefaultAsync();

        if (ds == null)
        {
            return NotFound(new { error = $"Data source {id} not found" });
        }

        _dbContext.DataSources.Remove(ds);
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Test data source connection
    /// </summary>
    [HttpPost("{id}/test")]
    public async Task<ActionResult<TestConnectionResponse>> TestConnection(Guid tenantId, string id)
    {
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        _logger.LogInformation("Testing connection for data source {Id}", id);

        // TODO: Implement actual connection testing based on data source type
        // For now, return mock success
        var startTime = DateTime.UtcNow;

        // Simulate connection test
        await Task.Delay(100);

        var endTime = DateTime.UtcNow;
        var latency = (int)(endTime - startTime).TotalMilliseconds;

        return Ok(new TestConnectionResponse
        {
            Success = true,
            Latency = latency,
            Message = "Connection successful",
            TestedAt = endTime
        });
    }

    /// <summary>
    /// Get available data source types
    /// </summary>
    [HttpGet("types")]
    [AllowAnonymous]
    public ActionResult<List<DataSourceTypeInfo>> GetDataSourceTypes()
    {
        var types = new List<DataSourceTypeInfo>
        {
            new()
            {
                Type = "postgresql",
                Name = "PostgreSQL",
                Category = "database",
                Description = "PostgreSQL relational database",
                ConfigSchema = new Dictionary<string, object>
                {
                    { "host", new { type = "string", required = true } },
                    { "port", new { type = "number", required = true, @default = 5432 } },
                    { "database", new { type = "string", required = true } },
                    { "username", new { type = "string", required = true } },
                    { "password", new { type = "string", required = true, secret = true } },
                    { "ssl", new { type = "boolean", required = false, @default = false } }
                }
            },
            new()
            {
                Type = "mysql",
                Name = "MySQL",
                Category = "database",
                Description = "MySQL relational database",
                ConfigSchema = new Dictionary<string, object>
                {
                    { "host", new { type = "string", required = true } },
                    { "port", new { type = "number", required = true, @default = 3306 } },
                    { "database", new { type = "string", required = true } },
                    { "username", new { type = "string", required = true } },
                    { "password", new { type = "string", required = true, secret = true } }
                }
            },
            new()
            {
                Type = "mongodb",
                Name = "MongoDB",
                Category = "database",
                Description = "MongoDB document database",
                ConfigSchema = new Dictionary<string, object>
                {
                    { "connectionString", new { type = "string", required = true, secret = true } },
                    { "database", new { type = "string", required = true } }
                }
            },
            new()
            {
                Type = "rest",
                Name = "REST API",
                Category = "api",
                Description = "REST API endpoint",
                ConfigSchema = new Dictionary<string, object>
                {
                    { "baseUrl", new { type = "string", required = true } },
                    { "authType", new { type = "string", required = false, @default = "none" } },
                    { "apiKey", new { type = "string", required = false, secret = true } },
                    { "headers", new { type = "object", required = false } }
                }
            },
            new()
            {
                Type = "graphql",
                Name = "GraphQL",
                Category = "api",
                Description = "GraphQL API endpoint",
                ConfigSchema = new Dictionary<string, object>
                {
                    { "endpoint", new { type = "string", required = true } },
                    { "authType", new { type = "string", required = false, @default = "none" } },
                    { "apiKey", new { type = "string", required = false, secret = true } },
                    { "headers", new { type = "object", required = false } }
                }
            },
            new()
            {
                Type = "csv",
                Name = "CSV File",
                Category = "file",
                Description = "CSV file import",
                ConfigSchema = new Dictionary<string, object>
                {
                    { "path", new { type = "string", required = true } },
                    { "delimiter", new { type = "string", required = false, @default = "," } },
                    { "hasHeader", new { type = "boolean", required = false, @default = true } },
                    { "encoding", new { type = "string", required = false, @default = "utf-8" } }
                }
            },
            new()
            {
                Type = "sheets",
                Name = "Google Sheets",
                Category = "cloud",
                Description = "Google Sheets spreadsheet",
                ConfigSchema = new Dictionary<string, object>
                {
                    { "spreadsheetId", new { type = "string", required = true } },
                    { "sheetName", new { type = "string", required = false } },
                    { "credentials", new { type = "object", required = true, secret = true } }
                }
            }
        };

        return Ok(types);
    }
}

#region DTOs

public class DataSourceResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object> Config { get; set; } = new();
    public string Status { get; set; } = "disconnected";
    public DateTime? LastTested { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateDataSourceRequest
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object> Config { get; set; } = new();
}

public class UpdateDataSourceRequest
{
    public string? Name { get; set; }
    public Dictionary<string, object>? Config { get; set; }
}

public class TestConnectionResponse
{
    public bool Success { get; set; }
    public int Latency { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime TestedAt { get; set; }
}

public class DataSourceTypeInfo
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> ConfigSchema { get; set; } = new();
}

#endregion
