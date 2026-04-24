using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data.Entities;
using static WelcomeMat.Data.SeedEntities;

namespace WelcomeMat.Data;

/// <summary>
/// What BaseSeedAsync built, so the callers that add to it need not look it back
/// up by name.
/// </summary>
public record SeedFixture
{
    public required User Admin { get; init; }

    public required User Guest { get; init; }

    public required User Guest2 { get; init; }

    public required Property Beach { get; init; }

    public required Property Cabin { get; init; }

    public required Reservation Reservation { get; init; }

    public string BeachIcalToken { get; init; } = string.Empty;
}

/// <summary>
/// An class representing the seed data a developer gets on a fresh database.
/// </summary>
public static class DevSeedService
{
    /// <summary>
    /// Empty every table, children before parents, so the foreign keys hold.
    /// </summary>
    /// <param name="db"></param>
    public static async Task WipeAsync(AppDbContext db)
    {
        // Children before parents, so the foreign keys hold all the way down.
        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM guestbook_entries;
            DELETE FROM guest_door_codes;
            DELETE FROM walking_paths;
            DELETE FROM favorite_spots;
            DELETE FROM blackout_dates;
            DELETE FROM reservations;
            DELETE FROM allowlist;
            DELETE FROM properties;
            DELETE FROM users;
            DELETE FROM sessions;
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE settings SET value = 'WelcomeMat' WHERE key = 'site_title';
            UPDATE settings SET value = '7' WHERE key = 'max_consecutive_nights';
            UPDATE settings SET value = '1' WHERE key = 'min_advance_notice_days';
            UPDATE settings SET value = '90' WHERE key = 'max_advance_booking_days';
            UPDATE settings SET value = lower(hex(randomblob(16))) WHERE key = 'admin_ical_token';
            """);
    }

    /// <summary>
    /// Seed the rows every dev scenario starts from.
    /// </summary>
    /// <param name="db"></param>
    /// <returns></returns>
    public static async Task<SeedFixture> BaseSeedAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var admin = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@test",
            Name = "Admin Tester",
            IsAdmin = true,
            CalendarToken = "admin-cal-token",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var guest = new User
        {
            Id = Guid.NewGuid(),
            Email = "guest@test",
            Name = "Guest Tester",
            CalendarToken = "guest-cal-token",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var guest2 = new User
        {
            Id = Guid.NewGuid(),
            Email = "guest2@test",
            Name = "Second Guest",
            CalendarToken = "guest2-cal-token",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.AddRange(admin, guest, guest2);

        foreach (var u in new[] { admin, guest, guest2 })
        {
            db.Allowlist.Add(new AllowlistEntry
            {
                Id = Guid.NewGuid(),
                Email = u.Email,
                AddedBy = admin.Id,
                CreatedAt = now,
            });
        }

        const string beachIcalToken = "beach-ical-token";
        const string cabinIcalToken = "cabin-ical-token";

        var beach = new Property
        {
            Id = Guid.NewGuid(),
            Name = "Beach House",
            Address = "1 Example Cove Rd, Anytown, ZZ 00000",
            WifiNetwork = "BeachWifi",
            WifiPassword = "sunny123",
            DoorCode = "4242",
            IcalToken = beachIcalToken,
            CheckinProcedures = "Pick up keys from lockbox\n   \nCode is the door code",
            CheckoutProcedures = "Strip the beds\nTake out trash",
            IsActive = true,
            Latitude = 0,
            Longitude = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var cabin = new Property
        {
            Id = Guid.NewGuid(),
            Name = "Mountain Cabin",
            Address = "2 Example Ridge Trail, Anytown, ZZ 00000",
            WifiNetwork = "CabinNet",
            WifiPassword = "pinecone",
            DoorCode = "1357",
            IcalToken = cabinIcalToken,
            CheckinProcedures = "Park in the gravel lot",
            CheckoutProcedures = "Lock all windows",
            IsActive = true,
            Latitude = 1,
            Longitude = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Properties.AddRange(beach, cabin);

        db.FavoriteSpots.Add(
            Spot(beach.Id, "Best Tacos", "3 Example Cove Rd, Anytown, ZZ 00000",
                "Get the al pastor.", 0.01, 0.01, 0, now));

        db.WalkingPaths.Add(new WalkingPath
        {
            Id = Guid.NewGuid(),
            PropertyId = beach.Id,
            Name = "Boardwalk Loop",
            Color = "#ff5722",
            Weight = 4,
            Opacity = 0.9,
            Coordinates = "[[0,0],[0.005,0.005],[0.01,0.01]]",
            SortOrder = 0,
            CreatedAt = now,
        });

        var checkIn = today.AddDays(10);
        var checkOut = today.AddDays(13);
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            UserId = guest.Id,
            PropertyId = beach.Id,
            CheckIn = checkIn,
            CheckOut = checkOut,
            Notes = "Seeded stay",
            CreatedAt = now,
        };
        db.Reservations.Add(reservation);

        await db.SaveChangesAsync();

        return new SeedFixture
        {
            Admin = admin,
            Guest = guest,
            Guest2 = guest2,
            Beach = beach,
            Cabin = cabin,
            Reservation = reservation,
            BeachIcalToken = beachIcalToken,
        };
    }

    /// <summary>
    /// The shape /test-support/reset hands back; the E2E tests read these names.
    /// </summary>
    /// <param name="f"></param>
    /// <returns></returns>
    public static object Payload(SeedFixture f) => new
    {
        adminEmail = f.Admin.Email,
        guestEmail = f.Guest.Email,
        guest2Email = f.Guest2.Email,
        adminCalToken = f.Admin.CalendarToken,
        guestCalToken = f.Guest.CalendarToken,
        beachIcalToken = f.BeachIcalToken,
        beachId = f.Beach.Id,
        beachName = f.Beach.Name,
        cabinId = f.Cabin.Id,
        cabinName = f.Cabin.Name,
        reservationId = f.Reservation.Id,
        reservationCheckIn = f.Reservation.CheckIn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        reservationCheckOut = f.Reservation.CheckOut.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Seed a fresh development database.
    /// </summary>
    /// <param name="db"></param>
    public static async Task SeedAsync(AppDbContext db)
    {
        var fixture = await BaseSeedAsync(db);
        var admin = fixture.Admin;
        var guest = fixture.Guest;
        var guest2 = fixture.Guest2;
        var beach = fixture.Beach;
        var cabin = fixture.Cabin;

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        db.FavoriteSpots.AddRange(
            Spot(beach.Id, "Surf Board Rentals", "4 Example Cove Rd, Anytown, ZZ 00000",
                "Hourly and full-day rates available.", 0.02, 0.02, 1, now),
            Spot(cabin.Id, "Bear Lake Trailhead", "5 Example Ridge Trail, Anytown, ZZ 00000",
                "Easy 2-mile loop with great views.", 1.02, 1.02, 0, now),
            Spot(cabin.Id, "Old Country Kitchen", "6 Example Ridge Trail, Anytown, ZZ 00000",
                "Hearty breakfasts, cash only.", 1.03, 1.03, 1, now));

        db.WalkingPaths.Add(new WalkingPath
        {
            Id = Guid.NewGuid(),
            PropertyId = cabin.Id,
            Name = "Pine Ridge Trail",
            Color = "#4caf50",
            Weight = 3,
            Opacity = 1.0,
            Coordinates = "[[1,1],[1.01,1.01],[1.02,1.02]]",
            SortOrder = 0,
            CreatedAt = now,
        });

        db.GuestDoorCodes.AddRange(
            new GuestDoorCode { UserId = guest.Id, PropertyId = beach.Id, DoorCode = "8888" },
            new GuestDoorCode { UserId = guest.Id, PropertyId = cabin.Id, DoorCode = "9999" },
            new GuestDoorCode { UserId = guest2.Id, PropertyId = beach.Id, DoorCode = "7777" },
            new GuestDoorCode { UserId = guest2.Id, PropertyId = cabin.Id, DoorCode = "6666" });

        db.BlackoutDates.AddRange(
            Blackout(beach.Id, today.AddDays(5), "Maintenance", admin.Id, now),
            Blackout(beach.Id, today.AddDays(6), "Maintenance", admin.Id, now),
            Blackout(cabin.Id, today.AddDays(7), "Maintenance", admin.Id, now),
            Blackout(cabin.Id, today.AddDays(8), "Maintenance", admin.Id, now));

        var future2CheckIn = today.AddDays(20);
        var future2CheckOut = today.AddDays(24);
        db.Reservations.Add(new Reservation
        {
            Id = Guid.NewGuid(),
            UserId = guest2.Id,
            PropertyId = cabin.Id,
            CheckIn = future2CheckIn,
            CheckOut = future2CheckOut,
            Notes = "Mountain escape",
            CreatedAt = now,
        });

        var pastBeachCheckIn = today.AddDays(-14);
        var pastBeachCheckOut = today.AddDays(-11);
        var pastBeachReservation = new Reservation
        {
            Id = Guid.NewGuid(),
            UserId = guest.Id,
            PropertyId = beach.Id,
            CheckIn = pastBeachCheckIn,
            CheckOut = pastBeachCheckOut,
            Notes = "Summer trip",
            CreatedAt = now.AddDays(-20),
        };

        var pastCabinCheckIn = today.AddDays(-30);
        var pastCabinCheckOut = today.AddDays(-27);
        var pastCabinReservation = new Reservation
        {
            Id = Guid.NewGuid(),
            UserId = guest.Id,
            PropertyId = cabin.Id,
            CheckIn = pastCabinCheckIn,
            CheckOut = pastCabinCheckOut,
            Notes = "Ski weekend",
            CreatedAt = now.AddDays(-35),
        };

        db.Reservations.AddRange(pastBeachReservation, pastCabinReservation);

        await db.SaveChangesAsync();

        db.GuestbookEntries.AddRange(
            new GuestbookEntry
            {
                Id = Guid.NewGuid(),
                ReservationId = pastBeachReservation.Id,
                PropertyId = beach.Id,
                UserId = guest.Id,
                Message = "Loved every minute! The sunset view is unbeatable.",
                CreatedAt = now.AddDays(-11),
            },
            new GuestbookEntry
            {
                Id = Guid.NewGuid(),
                ReservationId = pastCabinReservation.Id,
                PropertyId = cabin.Id,
                UserId = guest.Id,
                Message = "Perfect mountain getaway. The pine trees were so peaceful.",
                CreatedAt = now.AddDays(-27),
            });

        await db.SaveChangesAsync();
    }
}
