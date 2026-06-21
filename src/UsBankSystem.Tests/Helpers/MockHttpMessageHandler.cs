using System.Net;
using System.Text;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Helpers;

public class MockHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

/// <summary>
/// Routes requests by path prefix: first matching entry wins.
/// Useful for services that call multiple endpoints (e.g. /auth/token + /swift/message).
/// </summary>
public class RoutingMockHttpMessageHandler(IReadOnlyList<(string PathPrefix, HttpStatusCode StatusCode, string Body)> routes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.PathAndQuery ?? "";
        foreach (var (prefix, code, body) in routes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new HttpResponseMessage(code)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
    }
}