using Binah.Context.Engine;

namespace Binah.Context.Workers;

public class ContextEngineWorker : BackgroundService
{
    private readonly OntologyLoader _ontologyLoader;
    private readonly GraphBuilder _graphBuilder;
    private readonly ILogger<ContextEngineWorker> _logger;
    private readonly Guid _tenantId;
    
    public ContextEngineWorker(
        OntologyLoader ontologyLoader,
        GraphBuilder graphBuilder,
        ILogger<ContextEngineWorker> logger)
    {
        _ontologyLoader = ontologyLoader;
        _graphBuilder = graphBuilder;
        _logger = logger;
        _tenantId = Guid.Parse(Environment.GetEnvironmentVariable("TENANT_ID") ?? Guid.Empty.ToString());
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Context Engine starting for tenant {TenantId}", _tenantId);
        
        var ontology = await _ontologyLoader.LoadActiveOntologyAsync(_tenantId);
        if (ontology == null)
        {
            _logger.LogError("Failed to load ontology. Exiting.");
            return;
        }
        
        var graph = _graphBuilder.BuildFromOntology(ontology);
        _logger.LogInformation("Context Engine running. Graph has {NodeCount} nodes", graph.NodeCount);
        
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
