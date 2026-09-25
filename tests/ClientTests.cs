using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AlphaQuantum.AIToolsBlocklist;
using Xunit;

namespace AlphaQuantum.AIToolsBlocklist.Tests;

public class ClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body, Func<HttpRequestMessage, Task>? inspect = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (inspect is not null) await inspect(req);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    [Fact]
    public void EmptyKeyFails() => Assert.Throws<ArgumentException>(() => new AIToolsBlocklistClient(""));

    [Fact]
    public async Task SendsKeyAndParsesJson()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"ok\":true}", async req => { Assert.Equal("test-key", req.Headers.GetValues("X-API-Key").Single()); });
        var client = new AIToolsBlocklistClient("test-key", new HttpClient(handler));
        var result = await client.CheckAsync("example.com");
        Assert.True(result["ok"].GetBoolean());
    }

    [Fact]
    public async Task HttpErrorBecomesApiException()
    {
        var client = new AIToolsBlocklistClient("test-key", new HttpClient(new StubHandler(HttpStatusCode.TooManyRequests, "slow down")));
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.CheckAsync("example.com"));
        Assert.Equal(429, ex.StatusCode);
        Assert.Equal("slow down", ex.Body);
    }
}
