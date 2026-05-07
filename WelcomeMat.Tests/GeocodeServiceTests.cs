using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using WelcomeMat.Config;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class GeocodeServiceTests
{
    private static AppOptions DefaultOptions() => new()
    {
        GeocodingEnabled = true,
        GeocodingBaseUrl = new Uri("https://nominatim.openstreetmap.org/"),
    };

    private static IMemoryCache NewCache() =>
        new MemoryCache(new MemoryCacheOptions { SizeLimit = GeocodeService.MaxCachedAddresses });

    private static (GeocodeService Service, RecordingHttpMessageHandler Handler) BuildServiceWithHandler(
        RecordingHttpMessageHandler handler,
        AppOptions? options = null,
        GeocodeRateGate? rateGate = null,
        IMemoryCache? cache = null)
    {
        var http = new HttpClient(handler);
        var service = new GeocodeService(
            http,
            NullLogger<GeocodeService>.Instance,
            Options.Create(options ?? DefaultOptions()),
            rateGate ?? new GeocodeRateGate(),
            cache ?? NewCache());
        return (service, handler);
    }

    private static GeocodeService BuildService(
        HttpResponseMessage response, AppOptions? options = null, GeocodeRateGate? rateGate = null) =>
        BuildServiceWithHandler(new RecordingHttpMessageHandler(_ => response), options, rateGate).Service;

    [Test]
    public async Task ValidResponse_ReturnsLatLon()
    {
        var service = BuildService(Json("""[{"lat":"51.5","lon":"-0.1"}]"""));
        var result = await service.GeocodeAsync("London");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Lat, Is.EqualTo(51.5));
        Assert.That(result.Value.Lon, Is.EqualTo(-0.1));
    }

    [Test]
    public async Task Non200Response_ReturnsNull()
    {
        var service = BuildService(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var result = await service.GeocodeAsync("London");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task EmptyArray_ReturnsNull()
    {
        var service = BuildService(Json("[]"));
        var result = await service.GeocodeAsync("nowhere");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task MalformedJson_ReturnsNull()
    {
        var service = BuildService(Json("not-json"));
        var result = await service.GeocodeAsync("London");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task SameNormalizedAddress_CalledTwice_MakesOneOutboundRequest()
    {
        var handler = new RecordingHttpMessageHandler(_ => Json("""[{"lat":"1.0","lon":"2.0"}]"""));
        var service = BuildServiceWithHandler(handler).Service;

        var address = $"Unique Cache Address {Guid.NewGuid()}";
        var first = await service.GeocodeAsync(address);
        var second = await service.GeocodeAsync(address);

        Assert.That(first, Is.EqualTo(second));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SharedCache_IsReusedAcrossServiceInstances()
    {
        var handler = new RecordingHttpMessageHandler(_ => Json("""[{"lat":"5.0","lon":"6.0"}]"""));
        var sharedCache = NewCache();
        var serviceA = BuildServiceWithHandler(handler, cache: sharedCache).Service;
        var serviceB = BuildServiceWithHandler(handler, cache: sharedCache).Service;

        var address = $"Shared Cache Address {Guid.NewGuid()}";
        var first = await serviceA.GeocodeAsync(address);
        var second = await serviceB.GeocodeAsync(address);

        Assert.That(first, Is.EqualTo((5.0, 6.0)));
        Assert.That(second, Is.EqualTo(first));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task FailedLookup_IsNotCached_RetriesOnNextCall()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var service = BuildServiceWithHandler(handler).Service;

        var address = $"Failing Address {Guid.NewGuid()}";
        var first = await service.GeocodeAsync(address);
        var second = await service.GeocodeAsync(address);

        Assert.That(first, Is.Null);
        Assert.That(second, Is.Null);
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Disabled_ReturnsNullWithoutOutboundRequest()
    {
        var handler = new RecordingHttpMessageHandler(_ => Json("""[{"lat":"1.0","lon":"2.0"}]"""));
        var options = DefaultOptions() with { GeocodingEnabled = false };
        var service = BuildServiceWithHandler(handler, options).Service;

        var result = await service.GeocodeAsync($"Disabled Address {Guid.NewGuid()}");

        Assert.That(result, Is.Null);
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task AlternateProvider_UsesConfiguredBaseUrl_AndIsNotRateLimited()
    {
        var handler = new RecordingHttpMessageHandler(_ => Json("""[{"lat":"3.0","lon":"4.0"}]"""));
        var options = DefaultOptions() with { GeocodingBaseUrl = new Uri("https://custom-geocoder.example/") };
        var rateGate = new GeocodeRateGate();
        var sw = Stopwatch.StartNew();
        var service = BuildServiceWithHandler(handler, options, rateGate).Service;

        var result = await service.GeocodeAsync($"Alt Provider Address {Guid.NewGuid()}");
        sw.Stop();

        Assert.That(result, Is.EqualTo((3.0, 4.0)));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(handler.Requests[0].RequestUri!.Host, Is.EqualTo("custom-geocoder.example"));
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(500)));
    }

    [Test]
    public void Cancellation_PropagatesAndMakesNoOutboundRequest()
    {
        var handler = new RecordingHttpMessageHandler(_ => Json("""[{"lat":"1.0","lon":"2.0"}]"""));
        var service = BuildServiceWithHandler(handler).Service;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(
            async () => await service.GeocodeAsync($"Cancelled Address {Guid.NewGuid()}", cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task DefaultProviderRequests_CarryIdentifyingUserAgent()
    {
        var handler = new RecordingHttpMessageHandler(_ => Json("""[{"lat":"1.0","lon":"2.0"}]"""));
        var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WelcomeMat/1.0");
        var service = new GeocodeService(
            http, NullLogger<GeocodeService>.Instance, Options.Create(DefaultOptions()),
            new GeocodeRateGate(), NewCache());

        await service.GeocodeAsync($"User Agent Address {Guid.NewGuid()}");

        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(handler.Requests[0].Headers.UserAgent.ToString(), Does.Contain("WelcomeMat"));
    }

    [Test]
    public async Task TwoDifferentAddresses_ThroughSeparateServiceInstances_ShareOneRateGate_AndAreSpacedOneSecondApart()
    {
        var starts = new List<DateTime>();
        var handler = new RecordingHttpMessageHandler(_ =>
        {
            starts.Add(DateTime.UtcNow);
            return Json("""[{"lat":"1.0","lon":"2.0"}]""");
        });
        var sharedGate = new GeocodeRateGate();

        var serviceA = BuildServiceWithHandler(handler, null, sharedGate).Service;
        var serviceB = BuildServiceWithHandler(handler, null, sharedGate).Service;

        await serviceA.GeocodeAsync($"Address A {Guid.NewGuid()}");
        await serviceB.GeocodeAsync($"Address B {Guid.NewGuid()}");

        Assert.That(starts, Has.Count.EqualTo(2));
        Assert.That((starts[1] - starts[0]).TotalMilliseconds, Is.GreaterThanOrEqualTo(950));
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    public class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            this.Respond = respond;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(this.Respond(request));
        }


        private readonly Func<HttpRequestMessage, HttpResponseMessage> Respond;
    }
}
