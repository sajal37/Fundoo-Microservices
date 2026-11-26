using Confluent.Kafka;
using Fundoo.Email.Application;
using Fundoo.Email.Domain;
using System.Text.Json;

namespace Fundoo.Email.Api;

public class KafkaConsumerService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KafkaConsumerService> _logger;

    public KafkaConsumerService(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<KafkaConsumerService> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? bootstrapServers = _configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            _logger.LogWarning("Kafka:BootstrapServers is not configured.");
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
            _logger.LogInformation("Subscribed to email-topic on {Servers}", bootstrapServers);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);
                    if (result?.Message?.Value != null)
                    {
                        _logger.LogInformation("Consumed message from email-topic: {Msg}", result.Message.Value);
                        var emailMessage = JsonSerializer.Deserialize<EmailMessage>(
                            result.Message.Value,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (emailMessage != null)
                        {
                            using var scope = _serviceProvider.CreateScope();
                            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                            await emailSender.SendEmailAsync(emailMessage);
                            _logger.LogInformation("Successfully sent email to {To}", emailMessage.Email);
                        }
                    }
                }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning("ConsumeException: {Error}", ex.Error.Reason);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing email message");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KafkaConsumerService failed to initialize");
        }
    }
}
