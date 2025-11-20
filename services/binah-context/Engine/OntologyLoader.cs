using System.Text.Json;
using Binah.Ontology.Models.Ontology;

namespace Binah.Context.Engine;

public class OntologyLoader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OntologyLoader> _logger;
    
    public OntologyLoader(HttpClient httpClient, ILogger<OntologyLoader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }
    
    public async Task<OntologyVersion?> LoadActiveOntologyAsync(Guid tenantId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"api/tenants/{tenantId}/ontology/active");
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<OntologyVersion>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load ontology for tenant {TenantId}", tenantId);
            return null;
        }
    }
}
