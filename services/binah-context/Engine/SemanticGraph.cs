using System.Collections.Concurrent;
using Binah.Ontology.Models.Ontology;

namespace Binah.Context.Engine;

public class SemanticGraph
{
    private readonly ConcurrentDictionary<string, List<PropertyDefinition>> _nodeTypes = new();
    private readonly ConcurrentDictionary<string, object> _nodes = new();
    private readonly List<(string Source, string Target, string Type)> _edges = new();
    
    public void AddNodeType(string typeName, List<PropertyDefinition> properties)
    {
        _nodeTypes[typeName] = properties;
    }
    
    public void AddEdgeType(string source, string target, string type)
    {
        _edges.Add((source, target, type));
    }
    
    public void AddNode(string id, string type, Dictionary<string, object> properties)
    {
        if (!_nodeTypes.ContainsKey(type))
            throw new InvalidOperationException($"Node type {type} not defined in ontology");
        
        _nodes[id] = new { Type = type, Properties = properties };
    }
    
    public object? GetNode(string id)
    {
        return _nodes.TryGetValue(id, out var node) ? node : null;
    }
}
