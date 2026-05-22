using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Contriwork.MarketData.Internal;

/// <summary>Shared HTTP plumbing for REST-based adapters.</summary>
internal static class HttpHelpers
{
    /// <summary>Build a query-string suffix from a key/value bag.</summary>
    /// <param name="parameters">Query parameters; <c>null</c> values are skipped.</param>
    /// <returns>A leading-<c>?</c> query string, or empty when no parameters.</returns>
    public static string QueryString(IReadOnlyDictionary<string, string?>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return string.Empty;
        }

        var encoded = parameters
            .Where(p => p.Value is not null)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}");
        return "?" + string.Join("&", encoded);
    }

    /// <summary>
    /// Issue an HTTP GET and return the response as a <see cref="JsonDocument"/>.
    /// Maps 429 to <see cref="RateLimitedException"/> and other 4xx/5xx to
    /// <see cref="AdapterUnavailableException"/>; network failures translate
    /// the same way.
    /// </summary>
    /// <param name="client">HTTP client to use.</param>
    /// <param name="adapterId">Adapter id for error context.</param>
    /// <param name="url">Absolute URL.</param>
    /// <param name="headers">Optional request headers.</param>
    /// <param name="cancellationToken">Cancellation for the request.</param>
    /// <returns>The parsed JSON document; caller disposes.</returns>
    public static async Task<JsonDocument> GetJsonAsync(
        HttpClient client,
        string adapterId,
        string url,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AdapterUnavailableException($"adapter {adapterId} timed out", adapterId);
        }
        catch (HttpRequestException ex)
        {
            throw new AdapterUnavailableException(
                $"adapter {adapterId} network error: {ex.Message}",
                adapterId);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new RateLimitedException($"adapter {adapterId} returned HTTP 429", adapterId);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new AdapterUnavailableException(
                    $"adapter {adapterId} returned HTTP {(int)response.StatusCode}",
                    adapterId);
            }

            try
            {
                return await response.Content
                    .ReadFromJsonAsync<JsonDocument>(cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new AdapterUnavailableException(
                        $"adapter {adapterId} returned empty body",
                        adapterId);
            }
            catch (JsonException ex)
            {
                throw new AdapterUnavailableException(
                    $"adapter {adapterId} returned non-JSON body: {ex.Message}",
                    adapterId);
            }
        }
    }
}
