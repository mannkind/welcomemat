using Microsoft.AspNetCore.Builder;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing the security headers sent on every response.
/// </summary>
public static class SecurityHeadersPolicy
{
    /// <summary>
    /// One year, the minimum age the HSTS preload list accepts.
    /// </summary>
    private const int HSTS_MAX_AGE_SECONDS = 31536000;

    /// <summary>
    /// The CDN serving leaflet and the stylesheet.
    /// </summary>
    private const string CDN = "https://cdn.jsdelivr.net";

    /// <summary>
    /// Build the header policy.
    /// </summary>
    /// <returns></returns>
    public static HeaderPolicyCollection Build() => new HeaderPolicyCollection()
        .AddContentTypeOptionsNoSniff()
        .AddFrameOptionsDeny()
        .AddStrictTransportSecurityMaxAgeIncludeSubDomains(HSTS_MAX_AGE_SECONDS)
        .AddPermissionsPolicy(builder =>
        {
            builder.AddCamera().None();
            builder.AddMicrophone().None();
            builder.AddGeolocation().None();
            builder.AddPayment().None();
            builder.AddUsb().None();
        })
        .AddContentSecurityPolicy(builder =>
        {
            builder.AddDefaultSrc().Self();
            builder.AddScriptSrc().Self().WithNonce().UnsafeEval().From(CDN);
            builder.AddStyleSrc().Self().UnsafeInline().From(CDN);
            builder.AddImgSrc().Self().Data().From("https://tile.openstreetmap.org").From(CDN);
            builder.AddConnectSrc().Self();
            builder.AddObjectSrc().None();
            builder.AddBaseUri().Self();
            builder.AddFormAction().Self();
            builder.AddFrameAncestors().None();
        });

    /// <summary>
    /// Add the per-host redirect and the security headers to the pipeline.
    /// </summary>
    /// <param name="app"></param>
    /// <returns></returns>
    public static IApplicationBuilder UseWelcomeMatSecurityHeaders(this IApplicationBuilder app)
    {
        app.UseMiddleware<PerHostHttpsRedirectMiddleware>();
        app.UseSecurityHeaders(Build());
        return app;
    }
}
