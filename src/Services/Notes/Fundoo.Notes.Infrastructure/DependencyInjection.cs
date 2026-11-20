using Fundoo.Notes.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fundoo.Notes.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNotesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection =
            configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(redisConnection))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Redis is not configured.");
        }

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName = "Fundoo:";
        });

        services.AddScoped<NotesRepository>();

        services.AddScoped<INoteRepository>(
            provider => provider.GetRequiredService<NotesRepository>());

        services.AddScoped<ILabelRepository>(
            provider => provider.GetRequiredService<NotesRepository>());

        return services;
    }
}