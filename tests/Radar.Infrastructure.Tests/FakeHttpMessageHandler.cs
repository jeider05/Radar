using System.Net;
using System.Text;

namespace Radar.Infrastructure.Tests;

/// <summary>Returns canned responses and records the requests it receives. No network access.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public FakeHttpMessageHandler(HttpStatusCode statusCode, string body = "[]", Action<HttpResponseMessage>? configure = null)
        : this(_ => Response(statusCode, body, configure))
    {
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    public static HttpResponseMessage Response(HttpStatusCode statusCode, string body = "[]", Action<HttpResponseMessage>? configure = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        configure?.Invoke(response);
        return response;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        return Task.FromResult(_respond(request));
    }
}
