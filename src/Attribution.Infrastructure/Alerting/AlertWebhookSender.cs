using System.Net.Http.Json;

namespace Attribution.Infrastructure.Alerting;

// contracts/alert-webhook.md: "any 2xx within a short timeout counts as delivered;
// non-2xx or timeout is retried with backoff". HttpClient's own configured timeout
// (set on the named client in Program.cs) is the "short timeout"; this adds the retry.
public sealed class AlertWebhookSender : IAlertWebhookSender
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    private readonly HttpClient _httpClient;

    public AlertWebhookSender(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<NotificationDeliveryOutcome> SendAsync(string webhookUrl, AlertWebhookPayload payload, CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(RetryDelays[attempt - 1], cancellationToken);
            }

            try
            {
                var response = await _httpClient.PostAsJsonAsync(webhookUrl, payload, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return NotificationDeliveryOutcome.Ok;
                }

                lastError = new HttpRequestException($"Webhook responded {(int)response.StatusCode} {response.StatusCode}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
            }
        }

        return NotificationDeliveryOutcome.Failed(lastError?.Message ?? "Webhook delivery failed for an unknown reason.");
    }
}
