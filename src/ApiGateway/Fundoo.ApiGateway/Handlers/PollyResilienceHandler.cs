using Polly;
using Polly.Retry;

namespace Fundoo.ApiGateway.Handlers;

public class PollyResilienceHandler : DelegatingHandler
{
    private static readonly ResiliencePipeline<HttpResponseMessage> retryPipeline =
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(1),
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(response =>
                        (int)response.StatusCode >= 500 ||
                        response.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                        response.StatusCode == System.Net.HttpStatusCode.TooManyRequests),
                OnRetry = args =>
                {
                    args.Outcome.Result?.Dispose();
                    return default;
                }
            })
            .AddTimeout(TimeSpan.FromSeconds(5))
            .Build();

    private static readonly ResiliencePipeline<HttpResponseMessage> timeoutPipeline =
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddTimeout(TimeSpan.FromSeconds(5))
            .Build();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get)
        {
            return timeoutPipeline.ExecuteAsync(
                token => new ValueTask<HttpResponseMessage>(
                    base.SendAsync(request, token)),
                cancellationToken).AsTask();
        }

        return retryPipeline.ExecuteAsync(
            token => new ValueTask<HttpResponseMessage>(
                base.SendAsync(CloneRequest(request), token)),
            cancellationToken).AsTask();
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}