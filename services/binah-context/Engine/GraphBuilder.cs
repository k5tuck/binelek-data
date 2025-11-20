using Binah.Ontology.Models.Ontology;

namespace Binah.Context.Engine;

public class GraphBuilder
{
    private readonly ILogger<GraphBuilder> _logger;
    
    public GraphBuilder(ILogger<GraphBuilder> logger)
    {
        _logger = logger;
    }
    
    public SemanticGraph BuildFromOntology(OntologyVersion ontology)
    {
        var graph = new SemanticGraph();
        
        foreach (var entity in ontology.Entities)
        {
            graph.AddNodeType(entity.Name, entity.Properties);
        }
        
        foreach (var relationship in ontology.Relationships)
        {
            graph.AddEdgeType(relationship.Source, relationship.Target, relationship.Name);
        }
        
        _logger.LogInformation(
            "Built semantic graph with {NodeTypes} node types and {EdgeTypes} edge types",
            ontology.Entities.Count,
            ontology.Relationships.Count);
        
        return graph;
    }
}
