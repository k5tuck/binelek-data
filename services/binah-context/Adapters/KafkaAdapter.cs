using Confluent.Kafka;

namespace Binah.Context.Adapters;

public class KafkaAdapter : IDataAdapter
{
    private readonly string _topic;
    private readonly IConsumer<string, string> _consumer;
    private readonly ILogger<KafkaAdapter> _logger;
    
    public event Action<Dictionary<string, object>>? OnDataReceived;
    
    public KafkaAdapter(string topic, string bootstrapServers, ILogger<KafkaAdapter> logger)
    {
        _topic = topic;
        _logger = logger;
        
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"context-engine-{Guid.NewGuid()}",
            AutoOffsetReset = AutoOffsetReset.Earliest
        };
        
        _consumer = new ConsumerBuilder<string, string>(config).Build();
    }
    
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _consumer.Subscribe(_topic);
        _logger.LogInformation("Subscribed to Kafka topic: {Topic}", _topic);
        
        await Task.Run(() =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = _consumer.Consume(cancellationToken);
                    var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(result.Message.Value);
                    OnDataReceived?.Invoke(data!);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error consuming from Kafka");
                }
            }
        }, cancellationToken);
    }
    
    public Task StopAsync()
    {
        _consumer.Close();
        return Task.CompletedTask;
    }
}
