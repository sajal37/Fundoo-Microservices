using Confluent.Kafka;
using System.Text.Json;

namespace Fundoo.User.Api;

public class KafkaProducerService
{
    private readonly string? _bootstrapServers;

    public KafkaProducerService(IConfiguration configuration)
    {
        _bootstrapServers = configuration["Kafka:BootstrapServers"];
    }

    public async Task<bool> SendEmailAsync(string email, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(_bootstrapServers))
        {
            return false;
        }

        try
        {
            var config = new ProducerConfig
            {
                BootstrapServers = _bootstrapServers,
                MessageTimeoutMs = 3000
            };

            using var producer = new ProducerBuilder<Null, string>(config).Build();

            var payload = JsonSerializer.Serialize(new
            {
                Email = email,
                Subject = subject,
                Body = body
            });

            await producer.ProduceAsync("email-topic", new Message<Null, string> { Value = payload });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
