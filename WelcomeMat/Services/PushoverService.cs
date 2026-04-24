using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WelcomeMat.Config;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a managed way to notify the owners over Pushover.
/// </summary>
public class PushoverService : IPushoverService
{
    /// <summary>
    /// Initializes a new instance of the PushoverService class.
    /// </summary>
    /// <param name="http"></param>
    /// <param name="options"></param>
    /// <param name="logger"></param>
    public PushoverService(HttpClient http, IOptions<AppOptions> options, ILogger<PushoverService> logger)
    {
        this.Http = http;
        this.Options = options;
        this.Logger = logger;
    }

    /// <inheritdoc />
    public async Task SendAsync(string message, string? url = null, string? urlTitle = null)
    {
        var opts = this.Options.Value;
        var pairs = opts.PushoverTokens.Zip(opts.PushoverUserTokens).ToList();
        if (pairs.Count == 0)
        {
            return;
        }

        await Task.WhenAll(pairs.Select(p => this.SendToAsync(p.First, p.Second, message, url, urlTitle)));
    }

    /// <summary>
    /// Send to one token pair.
    /// </summary>
    /// <param name="token"></param>
    /// <param name="userToken"></param>
    /// <param name="message"></param>
    /// <param name="url"></param>
    /// <param name="urlTitle"></param>
    private async Task SendToAsync(string token, string userToken, string message, string? url, string? urlTitle)
    {
        var form = new Dictionary<string, string>
        {
            ["token"] = token,
            ["user"] = userToken,
            ["message"] = message,
        };

        if (!string.IsNullOrEmpty(url))
        {
            form["url"] = url;
        }

        if (!string.IsNullOrEmpty(urlTitle))
        {
            form["url_title"] = urlTitle;
        }

        try
        {
            var resp = await this.Http.PostAsync(
                "https://api.pushover.net/1/messages.json",
                new FormUrlEncodedContent(form));
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            this.Logger.LogWarning(
                "Pushover notification failed. Exception type: {ExceptionType}",
                ex.GetType().Name);
        }
    }

    /// <summary>
    /// The client used to reach the source.
    /// </summary>
    private readonly HttpClient Http;

    /// <summary>
    /// The application options.
    /// </summary>
    private readonly IOptions<AppOptions> Options;

    /// <summary>
    /// The logger used internally.
    /// </summary>
    private readonly ILogger<PushoverService> Logger;
}
