using Confluent.Kafka;
using System.Text.Json;

namespace Binah.Pipeline.Connectors;

/// <summary>
/// Connector for consuming messages from Kafka topics (as a source)
/// </summary>
public class KafkaConsumerConnector : IConnector
{
    private readonly ILogger<KafkaConsumerConnector> _logger;

    public KafkaConsumerConnector(ILogger<KafkaConsumerConnector> logger)
    {
        _logger = logger;
    }

    public async Task<List<Dictionary<string, object>>> ExtractAsync(Dictionary<string, string> config)
    {
        var bootstrapServers = config["bootstrapServers"];
        var topic = config["topic"];
        var groupId = config.GetValueOrDefault("groupId", $"pipeline-consumer-{Guid.NewGuid()}");
        var maxMessages = config.TryGetValue("maxMessages", out var maxStr) && int.TryParse(maxStr, out var max) ? max : 100;
        var timeoutSeconds = config.TryGetValue("timeoutSeconds", out var timeoutStr) && int.TryParse(timeoutStr, out var timeout) ? timeout : 30;

        var results = new List<Dictionary<string, object>>();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false // Manual commit for better control
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();

        try
        {
            consumer.Subscribe(topic);
            _logger.LogInformation("Subscribed to Kafka topic: {Topic} with group: {GroupId}", topic, groupId);

            var startTime = DateTime.UtcNow;
            var timeoutSpan = TimeSpan.FromSeconds(timeoutSeconds);

            while (results.Count < maxMessages)
            {
                // Check if we've exceeded the timeout
                if (DateTime.UtcNow - startTime > timeoutSpan)
                {
                    _logger.LogInformation("Timeout reached after {Seconds} seconds", timeoutSeconds);
                    break;
                }

                try
                {
                    var consumeResult = consumer.Consume(TimeSpan.FromSeconds(5));

                    if (consumeResult == null || consumeResult.Message == null)
                    {
                        // No more messages available
                        _logger.LogInformation("No more messages available in topic");
                        break;
                    }

                    // Parse the message
                    var message = consumeResult.Message.Value;
                    var data = ParseMessage(message);

                    if (data != null)
                    {
                        // Add Kafka metadata
                        data["_kafka_partition"] = consumeResult.Partition.Value;
                        data["_kafka_offset"] = consumeResult.Offset.Value;
                        data["_kafka_timestamp"] = consumeResult.Message.Timestamp.UtcDateTime;
                        data["_kafka_key"] = consumeResult.Message.Key ?? "";

                        results.Add(data);
                    }

                    // Commit the offset
                    consumer.Commit(consumeResult);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Error consuming from Kafka");
                    break;
                }
            }

            _logger.LogInformation("Consumed {Count} messages from Kafka topic: {Topic}", results.Count, topic);
            return await Task.FromResult(results);
        }
        finally
        {
            consumer.Close();
        }
    }

    /// <summary>
    /// Parse Kafka message (assumes JSON format)
    /// </summary>
    private Dictionary<string, object>? ParseMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(message);
            return data;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Kafka message as JSON, storing as raw text");
            return new Dictionary<string, object>
            {
                ["_raw_message"] = message
            };
        }
    }
}
