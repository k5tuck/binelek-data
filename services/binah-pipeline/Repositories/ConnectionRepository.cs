using Binah.Pipeline.Data;
using Binah.Pipeline.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Binah.Pipeline.Repositories;

/// <summary>
/// Repository implementation for Connection CRUD operations
/// </summary>
public class ConnectionRepository : IConnectionRepository
{
    private readonly PipelineDbContext _context;
    private readonly ILogger<ConnectionRepository> _logger;

    public ConnectionRepository(PipelineDbContext context, ILogger<ConnectionRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Connection> CreateAsync(Connection connection)
    {
        _logger.LogInformation("Creating connection {Name} of type {Type} for tenant {TenantId}",
            connection.Name, connection.Type, connection.TenantId);

        connection.CreatedAt = DateTime.UtcNow;
        connection.UpdatedAt = DateTime.UtcNow;

        _context.Connections.Add(connection);
        await _context.SaveChangesAsync();

        return connection;
    }

    public async Task<Connection?> GetByIdAsync(Guid connectionId, Guid tenantId)
    {
        return await _context.Connections
            .Where(c => c.Id == connectionId && c.TenantId == tenantId)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Connection>> GetByTenantAsync(Guid tenantId, int skip = 0, int limit = 50)
    {
        return await _context.Connections
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.UpdatedAt)
            .Skip(skip)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<Connection>> GetByTypeAsync(Guid tenantId, string type)
    {
        return await _context.Connections
            .Where(c => c.TenantId == tenantId && c.Type == type)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<List<Connection>> GetByStatusAsync(Guid tenantId, string status)
    {
        return await _context.Connections
            .Where(c => c.TenantId == tenantId && c.Status == status)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<Connection> UpdateAsync(Connection connection)
    {
        _logger.LogInformation("Updating connection {Id}", connection.Id);

        connection.UpdatedAt = DateTime.UtcNow;

        _context.Connections.Update(connection);
        await _context.SaveChangesAsync();

        return connection;
    }

    public async Task DeleteAsync(Guid connectionId, Guid tenantId)
    {
        _logger.LogInformation("Deleting connection {Id} for tenant {TenantId}", connectionId, tenantId);

        var connection = await GetByIdAsync(connectionId, tenantId);
        if (connection != null)
        {
            _context.Connections.Remove(connection);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsByNameAsync(Guid tenantId, string name, Guid? excludeId = null)
    {
        var query = _context.Connections
            .Where(c => c.TenantId == tenantId && c.Name == name);

        if (excludeId.HasValue)
        {
            query = query.Where(c => c.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }
}
