using System.Xml;
using System.Xml.Linq;

namespace Binah.Pipeline.Connectors;

/// <summary>
/// Connector for reading XML files
/// </summary>
public class XmlFileConnector : IConnector
{
    private readonly ILogger<XmlFileConnector> _logger;

    public XmlFileConnector(ILogger<XmlFileConnector> logger)
    {
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config)
    {
        var filePath = config["filePath"];

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"XML file not found: {filePath}");
        }

        // Get the element path (XPath-like syntax)
        var elementPath = config.GetValueOrDefault("elementPath", "");

        var results = new List<Dictionary<string, object>>();

        await Task.Run(() =>
        {
            var xmlDoc = XDocument.Load(filePath);

            IEnumerable<XElement> elements;

            if (string.IsNullOrEmpty(elementPath))
            {
                // If no path specified, use root's children
                elements = xmlDoc.Root?.Elements() ?? Enumerable.Empty<XElement>();
            }
            else
            {
                // Parse simple path like "root/items/item"
                var pathParts = elementPath.Split('/');
                XElement? current = xmlDoc.Root;

                foreach (var part in pathParts)
                {
                    if (current == null) break;
                    current = current.Element(part);
                }

                elements = current?.Elements() ?? Enumerable.Empty<XElement>();
            }

            foreach (var element in elements)
            {
                var row = ConvertElementToDictionary(element);
                results.Add(row);
            }
        });

        _logger.LogInformation("Extracted {Count} records from XML file: {FilePath}", results.Count, filePath);
        return results;
    }

    /// <summary>
    /// Convert XML element to dictionary
    /// </summary>
    private Dictionary<string, object> ConvertElementToDictionary(XElement element)
    {
        var dict = new Dictionary<string, object>();

        // Add attributes
        foreach (var attr in element.Attributes())
        {
            dict[attr.Name.LocalName] = ParseValue(attr.Value);
        }

        // Add child elements
        foreach (var child in element.Elements())
        {
            var childName = child.Name.LocalName;

            // Check if element has children or just text
            if (child.HasElements)
            {
                // Nested object
                dict[childName] = ConvertElementToDictionary(child);
            }
            else
            {
                // Simple value
                dict[childName] = ParseValue(child.Value);
            }
        }

        // If no children, add the element value itself
        if (!element.HasElements && !element.Attributes().Any())
        {
            return new Dictionary<string, object> { ["value"] = ParseValue(element.Value) };
        }

        return dict;
    }

    /// <summary>
    /// Parse string value to appropriate type
    /// </summary>
    private object ParseValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // Try int
        if (int.TryParse(value, out var intValue))
        {
            return intValue;
        }

        // Try long
        if (long.TryParse(value, out var longValue))
        {
            return longValue;
        }

        // Try decimal
        if (decimal.TryParse(value, out var decimalValue))
        {
            return decimalValue;
        }

        // Try bool
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        // Try datetime
        if (DateTime.TryParse(value, out var dateValue))
        {
            return dateValue;
        }

        // Default to string
        return value;
    }
}
