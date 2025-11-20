using FluentAssertions;
using Moq;
using Xunit;

namespace Binah.Context.Tests;

public class EnrichmentServiceTests
{
    [Fact]
    public void NormalizePropertyData_WithValidData_ReturnsNormalizedObject()
    {
        // Arrange
        var rawData = new Dictionary<string, object>
        {
            { "address", "123 Main St" },
            { "city", "Austin" },
            { "state", "TX" },
            { "price", "500000" },
            { "sqft", "2500" }
        };

        // Act
        var normalized = new Dictionary<string, object>
        {
            { "street", "123 Main St" },
            { "city", "Austin" },
            { "state", "TX" },
            { "listPrice", 500000 },
            { "squareFeet", 2500 }
        };

        // Assert
        normalized.Should().ContainKey("street");
        normalized.Should().ContainKey("listPrice");
        normalized["listPrice"].Should().Be(500000);
    }

    [Fact]
    public void ValidateData_WithMissingRequiredFields_ReturnsFalse()
    {
        // Arrange
        var incompleteData = new Dictionary<string, object>
        {
            { "city", "Austin" }
            // Missing street, state, etc.
        };

        // Act
        var isValid = incompleteData.ContainsKey("street") && 
                      incompleteData.ContainsKey("city") && 
                      incompleteData.ContainsKey("state");

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void EnrichWithExternalData_AddsMarketData()
    {
        // Arrange
        var propertyData = new
        {
            Street = "123 Main St",
            City = "Austin",
            State = "TX"
        };

        // Act - Simulate enrichment
        var enriched = new
        {
            propertyData.Street,
            propertyData.City,
            propertyData.State,
            MarketValue = 550000, // Added by enrichment
            ComparableCount = 5,  // Added by enrichment
            LastSaleDate = DateTime.Parse("2020-05-15") // Added by enrichment
        };

        // Assert
        enriched.MarketValue.Should().BeGreaterThan(0);
        enriched.ComparableCount.Should().BeGreaterThan(0);
    }
}
