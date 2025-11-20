using Binah.Pipeline.Models;

namespace Binah.Pipeline.Repositories;

/// <summary>
/// Repository interface for Connection CRUD operations
/// </summary>
public interface IConnectionRepository
{
    /// <summary>
    /// Create a new connection
    /// </summary>
    Task<Connection> CreateAsync(Connection connection);

    /// <summary>
    /// Get a connection by ID for a specific tenant
    /// </summary>
    Task<Connection?> GetByIdAsync(Guid connectionId, Guid tenantId);

    /// <summary>
    /// Get all connections for a tenant with pagination
    /// </summary>
    Task<List<Connection>> GetByTenantAsync(Guid tenantId, int skip = 0, int limit = 50);

    /// <summary>
    /// Get connections by type for a tenant
    /// </summary>
    Task<List<Connection>> GetByTypeAsync(Guid tenantId, string type);

    /// <summary>
    /// Get connections by status for a tenant
    /// </summary>
    Task<List<Connection>> GetByStatusAsync(Guid tenantId, string status);

    /// <summary>
    /// Update an existing connection
    /// </summary>
    Task<Connection> UpdateAsync(Connection connection);

    /// <summary>
    /// Delete a connection
    /// </summary>
    Task DeleteAsync(Guid connectionId, Guid tenantId);

    /// <summary>
    /// Check if a connection name already exists for a tenant
    /// </summary>
    Task<bool> ExistsByNameAsync(Guid tenantId, string name, Guid? excludeId = null);
}
