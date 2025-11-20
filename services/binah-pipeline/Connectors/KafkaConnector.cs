using Confluent.Kafka;
using System.Text.Json;

namespace Binah.Pipeline.Connectors;

public class KafkaConnector : IConnector
{
    private readonly ILogger<KafkaConnector> _logger;

    public KafkaConnector(ILogger<KafkaConnector> logger)
    {
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config)
    {
        // This connector is typically used as a destination, not source
        throw new NotImplementedException("Kafka connector is for destination only");
    }

    public async Task LoadAsync(List<Dictionary<string, object>> data, Dictionary<string, string> config)
    {
        var bootstrapServers = config["bootstrapServers"];
        var topic = config["topic"];

        var producerConfig = new ProducerConfig { BootstrapServers = bootstrapServers };

        using var producer = new ProducerBuilder<Null, string>(producerConfig).Build();

        foreach (var record in data)
        {
            var json = JsonSerializer.Serialize(record);
            await producer.ProduceAsync(topic, new Message<Null, string> { Value = json });
        }

        _logger.LogInformation("Loaded {Count} records to Kafka topic {Topic}", data.Count, topic);
    }

    /// <summary>
    /// Publish a single entity to Kafka with metadata
    /// </summary>
    public async Task<DeliveryResult<Null, string>> PublishAsync(
        Dictionary<string, object> entity,
        string topic,
        string bootstrapServers)
    {
        var producerConfig = new ProducerConfig { BootstrapServers = bootstrapServers };

        using var producer = new ProducerBuilder<Null, string>(producerConfig).Build();

        var json = JsonSerializer.Serialize(entity);
        var result = await producer.ProduceAsync(topic, new Message<Null, string> { Value = json });

        return result;
    }

    /// <summary>
    /// Publish multiple entities to Kafka in batch
    /// </summary>
    public async Task<int> PublishBatchAsync(
        List<Dictionary<string, object>> entities,
        string topic,
        string bootstrapServers,
        Action<int, int>? progressCallback = null)
    {
        var producerConfig = new ProducerConfig { BootstrapServers = bootstrapServers };

        using var producer = new ProducerBuilder<Null, string>(producerConfig).Build();

        int successCount = 0;
        int failCount = 0;

        for (int i = 0; i < entities.Count; i++)
        {
            try
            {
                var json = JsonSerializer.Serialize(entities[i]);
                await producer.ProduceAsync(topic, new Message<Null, string> { Value = json });
                successCount++;

                // Report progress
                progressCallback?.Invoke(successCount, failCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish entity {Index} to Kafka topic {Topic}", i, topic);
                failCount++;
                progressCallback?.Invoke(successCount, failCount);
            }
        }

        _logger.LogInformation(
            "Published {SuccessCount} entities to Kafka topic {Topic}, {FailCount} failed",
            successCount, topic, failCount);

        return successCount;
    }
}
