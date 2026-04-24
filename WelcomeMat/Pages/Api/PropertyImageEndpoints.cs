using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data;
using WelcomeMat.Services;

namespace WelcomeMat.Pages.Api;

/// <summary>
/// Extensions for mapping the property photo endpoint.
/// </summary>
public static class PropertyImageEndpointsExtensions
{
    /// <summary>
    /// Map the photo route. Photos are served from code, not wwwroot, so an
    /// inactive property's images stop being reachable.
    /// </summary>
    /// <param name="app"></param>
    public static void MapPropertyImageEndpoints(this WebApplication app)
    {
        app.MapGet("/images/{propertyId:guid}/{filename}", async Task<Results<FileStreamHttpResult, NotFound>> (
            Guid propertyId,
            string filename,
            AppDbContext db,
            PropertyImageService images,
            HttpContext http,
            CancellationToken ct) =>
        {
            var isActive = await db.Properties.AnyAsync(p => p.Id == propertyId && p.IsActive, ct);
            if (!isActive)
            {
                return TypedResults.NotFound();
            }

            var stream = images.OpenRead(propertyId, filename);
            if (stream is null)
            {
                return TypedResults.NotFound();
            }

            http.Response.Headers.CacheControl = "private, no-store";
            return TypedResults.File(stream, "image/jpeg");
        }).RequireAuthorization();
    }
}
