using Microsoft.AspNetCore.Mvc;
using Binah.Pipeline.Models;
using Binah.Pipeline.Repositories;
using Binah.Pipeline.Services;
using Microsoft.AspNetCore.Authorization;

namespace Binah.Pipeline.Controllers;

[ApiController]
[Route("api/tenants/{tenantId}/connections")]
[Authorize]
public class ConnectionsController : ControllerBase
{
    private readonly IConnectionRepository _repository;
    private readonly IConnectionService _connectionService;
    private readonly ILogger<ConnectionsController> _logger;

    public ConnectionsController(
        IConnectionRepository repository,
        IConnectionService connectionService,
        ILogger<ConnectionsController> logger)
    {
        _repository = repository;
        _connectionService = connectionService;
        _logger = logger;
    }

    /// <summary>
    /// Validate that the route tenantId matches the JWT tenant_id claim
    /// </summary>
    /// <returns>True if valid, False if mismatch or claim missing</returns>
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
    /// List all connections for tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<Connection>>> ListConnections(
        Guid tenantId,
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        List<Connection> connections;

        if (!string.IsNullOrEmpty(type))
        {
            connections = await _repository.GetByTypeAsync(tenantId, type);
        }
        else if (!string.IsNullOrEmpty(status))
        {
            connections = await _repository.GetByStatusAsync(tenantId, status);
        }
        else
        {
            connections = await _repository.GetByTenantAsync(tenantId, skip, limit);
        }

        return Ok(connections);
    }

    /// <summary>
    /// Get connection by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Connection>> GetConnection(Guid tenantId, Guid id)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var connection = await _repository.GetByIdAsync(id, tenantId);

        if (connection == null)
        {
            return NotFound(new { error = $"Connection {id} not found" });
        }

        return Ok(connection);
    }

    /// <summary>
    /// Create a new connection
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Connection>> CreateConnection(
        Guid tenantId,
        [FromBody] CreateConnectionRequest request)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        _logger.LogInformation("Creating connection {Name} for tenant {TenantId}", request.Name, tenantId);

        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Connection name is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            return BadRequest(new { error = "Connection type is required" });
        }

        // Check for duplicate name
        if (await _repository.ExistsByNameAsync(tenantId, request.Name))
        {
            return BadRequest(new { error = $"Connection with name '{request.Name}' already exists" });
        }

        // Validate configuration based on type
        var configValidation = _connectionService.ValidateConnectionConfig(request.Type, request.Config);
        if (configValidation != null)
        {
            return BadRequest(new { error = configValidation });
        }

        // Validate direction
        var validDirections = new[] { "import", "export", "bidirectional" };
        if (!validDirections.Contains(request.Direction.ToLowerInvariant()))
        {
            return BadRequest(new { error = $"Invalid direction. Must be one of: {string.Join(", ", validDirections)}" });
        }

        var connection = new Connection
        {
            TenantId = tenantId,
            Name = request.Name,
            Type = request.Type.ToLowerInvariant(),
            Direction = request.Direction.ToLowerInvariant(),
            Config = request.Config,
            Status = "pending",
            CreatedBy = User.Identity?.Name
        };

        connection = await _repository.CreateAsync(connection);

        return CreatedAtAction(nameof(GetConnection), new { tenantId, id = connection.Id }, connection);
    }

    /// <summary>
    /// Update an existing connection
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<Connection>> UpdateConnection(
        Guid tenantId,
        Guid id,
        [FromBody] UpdateConnectionRequest request)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var connection = await _repository.GetByIdAsync(id, tenantId);

        if (connection == null)
        {
            return NotFound(new { error = $"Connection {id} not found" });
        }

        // Update fields if provided
        if (request.Name != null)
        {
            // Check for duplicate name (excluding current connection)
            if (await _repository.ExistsByNameAsync(tenantId, request.Name, id))
            {
                return BadRequest(new { error = $"Connection with name '{request.Name}' already exists" });
            }
            connection.Name = request.Name;
        }

        if (request.Type != null)
        {
            connection.Type = request.Type.ToLowerInvariant();
        }

        if (request.Direction != null)
        {
            var validDirections = new[] { "import", "export", "bidirectional" };
            if (!validDirections.Contains(request.Direction.ToLowerInvariant()))
            {
                return BadRequest(new { error = $"Invalid direction. Must be one of: {string.Join(", ", validDirections)}" });
            }
            connection.Direction = request.Direction.ToLowerInvariant();
        }

        if (request.Status != null)
        {
            var validStatuses = new[] { "pending", "testing", "connected", "error", "disabled" };
            if (!validStatuses.Contains(request.Status.ToLowerInvariant()))
            {
                return BadRequest(new { error = $"Invalid status. Must be one of: {string.Join(", ", validStatuses)}" });
            }
            connection.Status = request.Status.ToLowerInvariant();
        }

        if (request.Config != null)
        {
            // Validate new configuration
            var configValidation = _connectionService.ValidateConnectionConfig(connection.Type, request.Config);
            if (configValidation != null)
            {
                return BadRequest(new { error = configValidation });
            }
            connection.Config = request.Config;
        }

        connection = await _repository.UpdateAsync(connection);

        return Ok(connection);
    }

    /// <summary>
    /// Delete a connection
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteConnection(Guid tenantId, Guid id)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var connection = await _repository.GetByIdAsync(id, tenantId);

        if (connection == null)
        {
            return NotFound(new { error = $"Connection {id} not found" });
        }

        await _repository.DeleteAsync(id, tenantId);

        _logger.LogInformation("Deleted connection {Id} for tenant {TenantId}", id, tenantId);

        return NoContent();
    }

    /// <summary>
    /// Test a connection
    /// </summary>
    [HttpPost("{id}/test")]
    public async Task<ActionResult<TestConnectionResponse>> TestConnection(Guid tenantId, Guid id)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var connection = await _repository.GetByIdAsync(id, tenantId);

        if (connection == null)
        {
            return NotFound(new { error = $"Connection {id} not found" });
        }

        _logger.LogInformation("Testing connection {Id} for tenant {TenantId}", id, tenantId);

        // Update status to testing
        connection.Status = "testing";
        await _repository.UpdateAsync(connection);

        // Test the connection
        var result = await _connectionService.TestConnectionAsync(connection);

        // Update status based on result
        connection.Status = result.Success ? "connected" : "error";
        connection.ErrorMessage = result.Success ? null : result.Message;
        await _repository.UpdateAsync(connection);

        return Ok(result);
    }

    /// <summary>
    /// Trigger a sync for a connection
    /// </summary>
    [HttpPost("{id}/sync")]
    public async Task<ActionResult<SyncConnectionResponse>> SyncConnection(Guid tenantId, Guid id)
    {
        // SECURITY: Validate tenant ID from JWT matches route parameter
        if (!ValidateTenantId(tenantId, out var jwtTenantId))
        {
            return string.IsNullOrEmpty(jwtTenantId)
                ? Unauthorized(new { error = "Tenant ID not found in token" })
                : Forbid();
        }

        var connection = await _repository.GetByIdAsync(id, tenantId);

        if (connection == null)
        {
            return NotFound(new { error = $"Connection {id} not found" });
        }

        if (connection.Status == "disabled")
        {
            return BadRequest(new { error = "Cannot sync a disabled connection" });
        }

        if (connection.Direction == "export")
        {
            return BadRequest(new { error = "Cannot sync an export-only connection. Use export endpoint instead." });
        }

        _logger.LogInformation("Syncing connection {Id} for tenant {TenantId}", id, tenantId);

        // Perform sync
        var result = await _connectionService.SyncConnectionAsync(connection);

        // Update last sync time
        if (result.Success)
        {
            connection.LastSyncAt = DateTime.UtcNow;
            connection.Status = "connected";
            connection.ErrorMessage = null;
        }
        else
        {
            connection.Status = "error";
            connection.ErrorMessage = result.Message;
        }

        await _repository.UpdateAsync(connection);

        return Ok(result);
    }

    /// <summary>
    /// Get supported connection types
    /// </summary>
    [HttpGet("types")]
    [AllowAnonymous]
    public ActionResult<List<string>> GetSupportedTypes()
    {
        return Ok(_connectionService.GetSupportedTypes());
    }
}
