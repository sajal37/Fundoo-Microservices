using Confluent.Kafka;
using Fundoo.Email.Application;
using Fundoo.Email.Domain;
using System.Text.Json;

namespace Fundoo.Email.Api;

public class KafkaConsumerService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public KafkaConsumerService(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? bootstrapServers = _configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            return;
        }

        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = "email-service-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        try
        {
            using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
            consumer.Subscribe("email-topic");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);
                    if (result?.Message?.Value != null)
                    {
                        var emailMessage = JsonSerializer.Deserialize<EmailMessage>(
                            result.Message.Value,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (emailMessage != null)
                        {
                            using var scope = _serviceProvider.CreateScope();
                            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                            await emailSender.SendEmailAsync(emailMessage);
                        }
                    }
                }
                catch (ConsumeException)
                {
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}
