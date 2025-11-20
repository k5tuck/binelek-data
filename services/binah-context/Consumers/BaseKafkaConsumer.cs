using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Binah.Context.Consumers;

/// <summary>
/// Base class for all Kafka consumers
/// Provides common consumer logic, offset commit handling, and error handling
/// </summary>
public abstract class BaseKafkaConsumer<TKey, TValue> : BackgroundService
{
    protected readonly ILogger Logger;
    protected readonly IConfiguration Configuration;
    protected IConsumer<TKey, TValue>? Consumer;
    protected readonly string Topic;
    protected readonly string GroupId;

    protected BaseKafkaConsumer(
        ILogger logger,
        IConfiguration configuration,
        string topic,
        string groupId)
    {
        Logger = logger;
        Configuration = configuration;
        Topic = topic;
        GroupId = groupId;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var kafkaBootstrapServers = Configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

        var config = new ConsumerConfig
        {
            BootstrapServers = kafkaBootstrapServers,
            GroupId = GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true
        };

        Consumer = new ConsumerBuilder<TKey, TValue>(config)
            .SetValueDeserializer(GetValueDeserializer())
            .Build();

        Consumer.Subscribe(Topic);

        Logger.LogInformation(
            "Kafka consumer started. Topic: {Topic}, GroupId: {GroupId}, Servers: {Servers}",
            Topic, GroupId, kafkaBootstrapServers
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Use timeout-based consume to avoid blocking the thread pool
                var consumeResult = Consumer.Consume(TimeSpan.FromSeconds(1));

                if (consumeResult?.Message == null)
                {
                    // Yield control periodically when no messages are available
                    await Task.Delay(100, stoppingToken);
                    continue;
                }

                await ProcessMessageAsync(consumeResult, stoppingToken);

                // Commit offset after successful processing
                Consumer.Commit(consumeResult);
            }
            catch (ConsumeException ex)
            {
                Logger.LogError(ex, "Kafka consume error on topic {Topic}", Topic);
                await HandleConsumeErrorAsync(ex, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                Logger.LogInformation("Kafka consumer {GroupId} stopping", GroupId);
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing Kafka message from topic {Topic}", Topic);
                await HandleProcessingErrorAsync(ex, stoppingToken);
            }
        }

        Consumer?.Close();
    }

    /// <summary>
    /// Process a consumed message (implemented by derived classes)
    /// </summary>
    protected abstract Task ProcessMessageAsync(
        ConsumeResult<TKey, TValue> consumeResult,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Get the value deserializer (override if custom deserialization needed)
    /// </summary>
    protected virtual IDeserializer<TValue>? GetValueDeserializer()
    {
        // Default: JSON deserializer for string values
        if (typeof(TValue) == typeof(string))
        {
            return null; // Use default string deserializer
        }

        throw new NotImplementedException(
            "Override GetValueDeserializer() for custom value types"
        );
    }

    /// <summary>
    /// Handle consume errors (override for custom error handling)
    /// </summary>
    protected virtual Task HandleConsumeErrorAsync(
        ConsumeException ex,
        CancellationToken cancellationToken
    )
    {
        // Default: just log the error
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle message processing errors (override for custom error handling)
    /// </summary>
    protected virtual Task HandleProcessingErrorAsync(
        Exception ex,
        CancellationToken cancellationToken
    )
    {
        // Default: continue processing next message
        return Task.CompletedTask;
    }

    /// <summary>
    /// Send failed message to dead letter queue
    /// </summary>
    protected async Task SendToDeadLetterQueueAsync(
        string originalTopic,
        string message,
        Exception error,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var dlqTopic = $"{originalTopic}.dlq";

            var dlqMessage = new
            {
                originalTopic,
                originalMessage = message,
                error = error.Message,
                stackTrace = error.StackTrace,
                timestamp = DateTime.UtcNow
            };

            var kafkaBootstrapServers = Configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

            var producerConfig = new ProducerConfig
            {
                BootstrapServers = kafkaBootstrapServers
            };

            using var producer = new ProducerBuilder<Null, string>(producerConfig).Build();

            await producer.ProduceAsync(
                dlqTopic,
                new Message<Null, string>
                {
                    Value = JsonSerializer.Serialize(dlqMessage)
                },
                cancellationToken
            );

            Logger.LogInformation(
                "Sent failed message to dead letter queue: {DlqTopic}",
                dlqTopic
            );
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send message to dead letter queue");
        }
    }

    public override void Dispose()
    {
        Consumer?.Dispose();
        base.Dispose();
    }
}
