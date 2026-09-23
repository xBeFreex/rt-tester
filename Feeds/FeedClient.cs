using Google.Protobuf;
using TransitRealtime;

namespace rt_tester.Feeds;

public sealed record FeedFetchResult(
    string Url,
    bool Ok,
    int? StatusCode,
    string? Error,
    FeedMessage? Feed);

public sealed class FeedClient(IHttpClientFactory httpClientFactory, ILogger<FeedClient> logger)
{
    public async Task<FeedFetchResult> FetchAsync(string? url, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new FeedFetchResult(url ?? "", false, null, "not configured", null);

        using var client = httpClientFactory.CreateClient(nameof(FeedClient));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            using var response = await client.GetAsync(url, cts.Token);
            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
                return new FeedFetchResult(url, false, (int)response.StatusCode, $"HTTP {(int)response.StatusCode}", null);

            var feed = FeedMessage.Parser.ParseFrom(bytes);
            return new FeedFetchResult(url, true, (int)response.StatusCode, null, feed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or InvalidProtocolBufferException)
        {
            logger.LogWarning(ex, "Failed to fetch/decode feed from {Url}", url);
            return new FeedFetchResult(url, false, null, ex.Message, null);
        }
    }
}
