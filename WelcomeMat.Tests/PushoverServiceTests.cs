using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using WelcomeMat.Config;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class PushoverServiceTests
{
    private static (PushoverService service, CapturingHttpMessageHandler handler) Build(
        string[] tokens, string[] userTokens)
    {
        var handler = new CapturingHttpMessageHandler();
        var http = new HttpClient(handler);
        var opts = Options.Create(new AppOptions
        {
            PushoverTokens = tokens,
            PushoverUserTokens = userTokens,
        });
        return (new PushoverService(http, opts, NullLogger<PushoverService>.Instance), handler);
    }

    [Test]
    public async Task NoTokens_SendsNoRequests()
    {
        var (service, handler) = Build([], []);
        await service.SendAsync("hello");
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task SinglePair_SendsOneRequest()
    {
        var (service, handler) = Build(["tok1"], ["usr1"]);
        await service.SendAsync("hello");
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        Assert.That(body, Does.Contain("message=hello"));
    }

    [Test]
    public async Task MultiplePairs_SendsOneRequestPerPair()
    {
        var (service, handler) = Build(["tok1", "tok2"], ["usr1", "usr2"]);
        await service.SendAsync("hello");
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task WithUrl_IncludesUrlInBody()
    {
        var (service, handler) = Build(["tok1"], ["usr1"]);
        await service.SendAsync("msg", url: "https://example.com", urlTitle: "View");
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        Assert.That(body, Does.Contain("url=https"));
        Assert.That(body, Does.Contain("url_title=View"));
    }

    private class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":1}"),
            });
        }
    }
}
