using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WelcomeMat.Config;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;

namespace WelcomeMat.Pages.Api;

/// <summary>
/// Extensions for mapping the endpoints the E2E tests drive. Only mapped in
/// Testing.
/// </summary>
public static class TestSupportEndpointsExtensions
{
    /// <summary>
    /// Stays for guest@test at the Beach House, offset from that property's today.
    /// The E2E tests ask by name rather than send dates over the wire.
    /// </summary>
    private static readonly (string Route, int CheckIn, int CheckOut, string Notes)[] ReservationScenarios =
    [
        ("seed-past-reservation", -10, -7, "Seeded past stay"),
        ("seed-older-past-reservation", -20, -17, "Seeded older past stay"),
        ("seed-ending-today-reservation", -2, 0, "Seeded stay ending today"),
        ("seed-ending-tomorrow-reservation", -2, 1, "Seeded stay ending tomorrow"),
        ("seed-guestbook-reservation", -4, -1, "Seeded guestbook stay"),
        ("seed-guestbook-boundary-reservation", -17, -14, "Seeded guestbook boundary stay"),
        ("seed-current-reservation", -1, 5, "Seeded currently-active stay"),
    ];

    /// <summary>
    /// Map the reset and seed routes the E2E tests drive.
    /// </summary>
    /// <param name="app"></param>
    public static void MapTestSupportEndpoints(this WebApplication app)
    {
        app.MapGet("/test-auth/login", async (string email, string? returnUrl, AppDbContext db, HttpContext http) =>
        {
            var normalizedEmail = EmailNormalizer.Normalize(email);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
            if (user is null)
            {
                return Results.NotFound($"No seeded user '{normalizedEmail}'. POST /test-support/reset first.");
            }

            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                AuthClaims.BuildPrincipal(user));

            var redirect = !string.IsNullOrEmpty(returnUrl) && Uri.TryCreate(returnUrl, UriKind.Relative, out _)
                ? returnUrl
                : "/";
            return Results.Redirect(redirect);
        }).AllowAnonymous();

        app.MapPost("/test-support/reset", async (AppDbContext db) =>
        {
            var seed = await ResetAndSeedAsync(db);
            return Results.Ok(seed);
        }).AllowAnonymous();

        app.MapPost("/test-support/seed-demo", async (AppDbContext db, IOptions<AppOptions> options) =>
        {
            var seed = await DemoSeedService.SeedAsync(db, options.Value.ImagesPath);
            return Results.Ok(seed);
        }).AllowAnonymous();

        foreach (var (route, checkIn, checkOut, notes) in ReservationScenarios)
        {
            app.MapPost($"/test-support/{route}", async (
                AppDbContext db, PropertyClock clock, ReservationCoordinator coordinator) =>
            {
                var guest = await db.Users.FirstOrDefaultAsync(u => u.Email == "guest@test");
                var beach = await db.Properties.FirstOrDefaultAsync(p => p.Name == "Beach House");
                if (guest is null || beach is null)
                {
                    return Results.NotFound("Seed first: POST /test-support/reset.");
                }

                var today = clock.Today(beach.TimeZone);
                return await SeedReservationAsync(
                    coordinator, guest, beach.Id,
                    today.AddDays(checkIn), today.AddDays(checkOut), notes);
            }).AllowAnonymous();
        }
    }

    /// <summary>
    /// Wipe and reseed, handing back the ids the tests need.
    /// </summary>
    /// <param name="db"></param>
    /// <returns></returns>
    private static async Task<object> ResetAndSeedAsync(AppDbContext db)
    {
        await DevSeedService.WipeAsync(db);
        return DevSeedService.Payload(await DevSeedService.BaseSeedAsync(db));
    }

    /// <summary>
    /// Book a seeded stay, skipping the rules the scenario is not testing.
    /// </summary>
    /// <param name="coordinator"></param>
    /// <param name="guest"></param>
    /// <param name="propertyId"></param>
    /// <param name="checkIn"></param>
    /// <param name="checkOut"></param>
    /// <param name="notes"></param>
    /// <returns></returns>
    private static async Task<IResult> SeedReservationAsync(
        ReservationCoordinator coordinator, User guest, Guid propertyId,
        DateOnly checkIn, DateOnly checkOut, string notes)
    {
        var request = new ReservationCoordinator.CreateRequest
        {
            PropertyId = propertyId,
            CheckIn = checkIn,
            CheckOut = checkOut,
            Notes = notes,
            TargetUserId = null,
        };
        var result = await coordinator.CreateAsync(
            request, AuthClaims.BuildPrincipal(guest), bypassValidation: true);

        if (result.Status != ReservationMutationStatus.Success || result.Reservation is not { } reservation)
        {
            return Results.Problem($"Failed to seed reservation: {result.Status}.");
        }

        return Results.Ok(new
        {
            id = reservation.Id,
            checkIn = checkIn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            checkOut = checkOut.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        });
    }
}
