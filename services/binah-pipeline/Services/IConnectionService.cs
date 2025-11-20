using Binah.Pipeline.Models;

namespace Binah.Pipeline.Services;

/// <summary>
/// Service interface for connection business logic including testing and syncing
/// </summary>
public interface IConnectionService
{
    /// <summary>
    /// Test a connection's configuration
    /// </summary>
    Task<TestConnectionResponse> TestConnectionAsync(Connection connection);

    /// <summary>
    /// Trigger a sync operation for a connection
    /// </summary>
    Task<SyncConnectionResponse> SyncConnectionAsync(Connection connection);

    /// <summary>
    /// Validate connection configuration based on type
    /// </summary>
    string? ValidateConnectionConfig(string type, Dictionary<string, object> config);

    /// <summary>
    /// Get supported connection types
    /// </summary>
    List<string> GetSupportedTypes();
}
