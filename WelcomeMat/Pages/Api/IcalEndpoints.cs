using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data;
using WelcomeMat.Services;

namespace WelcomeMat.Pages.Api;

/// <summary>
/// Extensions for mapping the calendar feed endpoint.
/// </summary>
public static class IcalEndpointsExtensions
{
    /// <summary>
    /// Map /ical/{token}. One route serves three feeds; the token decides
    /// whether it is a user's, a property's, or every reservation.
    /// </summary>
    /// <param name="app"></param>
    public static void MapIcalEndpoints(this WebApplication app)
    {
        app.MapGet("/ical/{token}", async (
            string token, AppDbContext db, HttpContext http, ReservationCalendarService calendar) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";

            if (string.IsNullOrEmpty(token))
            {
                return Results.NotFound();
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.CalendarToken == token);
            if (user is not null)
            {
                var reservations = await db.Reservations
                    .Include(r => r.Property)
                    .Where(r => r.UserId == user.Id && r.DeletedAt == null)
                    .OrderBy(r => r.CheckIn)
                    .ToListAsync();
                var events = await calendar.BuildEventsAsync(reservations, includeGuest: false);
                var ical = IcalService.Build("My Reservations", events);
                return Results.Content(ical, "text/calendar; charset=utf-8");
            }

            var property = await db.Properties.FirstOrDefaultAsync(p => p.IcalToken == token);
            if (property is not null)
            {
                var propertyReservations = await db.Reservations
                    .Include(r => r.Property)
                    .Include(r => r.User)
                    .Where(r => r.PropertyId == property.Id && r.DeletedAt == null)
                    .OrderBy(r => r.CheckIn)
                    .ToListAsync();
                var propertyEvents = await calendar.BuildEventsAsync(propertyReservations, includeGuest: true);
                var propertyIcal = IcalService.Build(property.Name, propertyEvents);
                return Results.Content(propertyIcal, "text/calendar; charset=utf-8");
            }

            var adminToken = await db.Settings.FirstOrDefaultAsync(s => s.Key == "admin_ical_token");
            if (adminToken is not null && adminToken.Value == token)
            {
                var allReservations = await db.Reservations
                    .Include(r => r.Property)
                    .Include(r => r.User)
                    .Where(r => r.DeletedAt == null)
                    .OrderBy(r => r.CheckIn)
                    .ToListAsync();
                var adminEvents = await calendar.BuildEventsAsync(allReservations, includeGuest: true);
                var adminIcal = IcalService.Build("All Reservations", adminEvents);
                return Results.Content(adminIcal, "text/calendar; charset=utf-8");
            }

            return Results.NotFound();
        }).AllowAnonymous();
    }
}
