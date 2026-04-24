using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data;

namespace WelcomeMat.Pages.Api;

/// <summary>
/// Extensions for mapping the health endpoint.
/// </summary>
public static class HealthEndpointsExtensions
{
    /// <summary>
    /// Map /healthz. Healthy means the database answers a read, not just that
    /// the process is up.
    /// </summary>
    /// <param name="app"></param>
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/healthz", async (AppDbContext db, CancellationToken ct) =>
        {
            try
            {
                await db.Settings.AsNoTracking().OrderBy(s => s.Key).Take(1).ToListAsync(ct);
                return Results.Json(new { status = "healthy" });
            }
            catch
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }).AllowAnonymous();
    }
}
