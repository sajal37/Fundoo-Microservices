using Confluent.Kafka;
using System.Text.Json;

namespace Fundoo.User.Api;

public class KafkaProducerService
{
    private readonly string? _bootstrapServers;
    private readonly ILogger<KafkaProducerService> _logger;

    public KafkaProducerService(IConfiguration configuration, ILogger<KafkaProducerService> logger)
    {
        _bootstrapServers = configuration["Kafka:BootstrapServers"];
        _logger = logger;
    }

    public async Task<bool> SendEmailAsync(string email, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(_bootstrapServers))
        {
            _logger.LogWarning("Kafka:BootstrapServers is empty, skipping Kafka producer.");
            return false;
        }

        try
        {
            var config = new ProducerConfig
            {
                BootstrapServers = _bootstrapServers,
                MessageTimeoutMs = 5000
            };

            using var producer = new ProducerBuilder<Null, string>(config).Build();

            var payload = JsonSerializer.Serialize(new
            {
                Email = email,
                Subject = subject,
                Body = body
            });

            var deliveryResult = await producer.ProduceAsync("email-topic", new Message<Null, string> { Value = payload });
            _logger.LogInformation("Produced email event to {Topic} partition {Partition} offset {Offset}", 
                deliveryResult.Topic, deliveryResult.Partition.Value, deliveryResult.Offset.Value);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to produce email message to Kafka.");
            return false;
        }
    }
}
