using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Contriwork.MarketData.Tests.Adapters;

/// <summary>
/// Test-only HTTP handler. Tests register a route → response map; the
/// handler matches incoming requests by URL prefix (path + query). Unknown
/// routes return 404.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Route> routes = [];

    /// <summary>Captured request inspector — last URL the handler served.</summary>
    public string? LastUrl { get; private set; }

    /// <summary>Captured request inspector — last headers the handler saw.</summary>
    public HttpRequestHeaders? LastHeaders { get; private set; }

    /// <summary>Register a JSON-body response for any URL containing <paramref name="match"/>.</summary>
    /// <param name="match">Substring of the request URL.</param>
    /// <param name="json">Body to return.</param>
    /// <param name="statusCode">HTTP status code.</param>
    /// <returns>This handler for chaining.</returns>
    public FakeHttpMessageHandler RespondTo(string match, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        this.routes.Add(new Route(match, statusCode, json));
        return this;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;
        this.LastUrl = url;
        this.LastHeaders = request.Headers;

        foreach (var route in this.routes)
        {
            if (url.Contains(route.Match, StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(route.Status)
                {
                    Content = new StringContent(route.Body, Encoding.UTF8, "application/json"),
                };
                return Task.FromResult(response);
            }
        }

        var notFound = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"error\":\"not found\"}", Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(notFound);
    }

    private sealed record Route(string Match, HttpStatusCode Status, string Body);
}
