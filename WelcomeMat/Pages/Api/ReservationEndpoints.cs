using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data;
using WelcomeMat.Services;

namespace WelcomeMat.Pages.Api;

/// <summary>
/// Extensions for mapping the reservation endpoints.
/// </summary>
public static class ReservationEndpointsExtensions
{
    /// <summary>
    /// Map the reservation create, change, and cancel routes.
    /// </summary>
    /// <param name="app"></param>
    public static void MapReservationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/reservations")
            .RequireAuthorization();

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext ctx,
            AppDbContext db) =>
        {
            var userId = Guid.Parse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());
            var isAdmin = ctx.User.HasClaim("is_admin", "true");

            var r = await db.Reservations
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt == null);

            if (r is null)
            {
                return Results.NotFound();
            }

            if (!isAdmin && r.UserId != userId)
            {
                return Results.Forbid();
            }

            return Results.Ok(new
            {
                id = r.Id,
                checkIn = r.CheckIn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                checkOut = r.CheckOut.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                notes = r.Notes,
                userName = r.User?.Name ?? "",
            });
        });

        group.MapMethods("/{id:guid}", ["PATCH"], async (
            Guid id,
            HttpContext ctx,
            IAntiforgery antiforgery,
            ReservationCoordinator coordinator) =>
        {
            if (await RejectIfForgedAsync(antiforgery, ctx) is { } forged)
            {
                return forged;
            }

            var notes = ctx.Request.Form["notes"].FirstOrDefault() ?? "";
            if (!TryParseDate(ctx.Request.Form["check_in"].FirstOrDefault(), out var checkIn) ||
                !TryParseDate(ctx.Request.Form["check_out"].FirstOrDefault(), out var checkOut))
            {
                return Results.BadRequest("Check-in and check-out dates are required.");
            }

            var result = await coordinator.ModifyAsync(
                new ReservationCoordinator.ModifyRequest
                {
                    ReservationId = id,
                    CheckIn = checkIn,
                    CheckOut = checkOut,
                    Notes = notes,
                },
                ctx.User, ctx.RequestAborted);

            return ResultFor(ctx, result);
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext ctx,
            IAntiforgery antiforgery,
            ReservationCoordinator coordinator) =>
        {
            if (await RejectIfForgedAsync(antiforgery, ctx) is { } forged)
            {
                return forged;
            }

            var result = await coordinator.CancelAsync(id, ctx.User, ctx.RequestAborted);
            return ResultFor(ctx, result);
        });

        app.MapDelete("/admin/reservations/{id:guid}", async (
            Guid id,
            HttpContext ctx,
            IAntiforgery antiforgery,
            ReservationCoordinator coordinator) =>
        {
            if (await RejectIfForgedAsync(antiforgery, ctx) is { } forged)
            {
                return forged;
            }

            if (!ctx.User.HasClaim("is_admin", "true"))
            {
                return Results.Forbid();
            }

            var result = await coordinator.CancelAsync(id, ctx.User, ctx.RequestAborted);
            return ResultFor(ctx, result);
        }).RequireAuthorization("AdminOnly");
    }

    /// <summary>
    /// Turn a mutation outcome into the response, fetch or full page.
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="result"></param>
    /// <returns></returns>
    private static IResult ResultFor(HttpContext ctx, ReservationMutationResult result)
    {
        switch (result.Status)
        {
            case ReservationMutationStatus.Success:
                var r = result.Reservation!;
                var redirectUrl = $"/calendar/{r.PropertyId}?year={r.CheckIn.Year}&month={r.CheckIn.Month}";
                return FetchRedirect(ctx, redirectUrl);
            case ReservationMutationStatus.NotFound:
                return Results.NotFound();
            case ReservationMutationStatus.Forbidden:
                return Results.Forbid();
            case ReservationMutationStatus.Conflict:
                return Results.Conflict(result.Error);
            case ReservationMutationStatus.ValidationError:
            default:
                return Results.BadRequest(result.Error);
        }
    }

    /// <summary>
    /// Reject a request whose antiforgery token does not check out; null when it does.
    /// </summary>
    /// <param name="antiforgery"></param>
    /// <param name="ctx"></param>
    /// <returns></returns>
    internal static async Task<IResult?> RejectIfForgedAsync(IAntiforgery antiforgery, HttpContext ctx)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(ctx);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest("Invalid or missing antiforgery token.");
        }
    }

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>
    /// Redirect; fetch callers get the target in a header.
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="url"></param>
    /// <returns></returns>
    private static IResult FetchRedirect(HttpContext ctx, string url)
    {
        if (Pages.AjaxResults.IsFetch(ctx.Request))
        {
            ctx.Response.Headers["X-Redirect"] = url;
            return Results.Ok();
        }
        return Results.Redirect(url);
    }
}
