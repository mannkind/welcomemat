using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;
using static WelcomeMat.Data.SeedEntities;

namespace WelcomeMat.Data;

/// <summary>
/// An class representing the seed data behind the screenshots in the README.
/// </summary>
public static class DemoSeedService
{
    private const double BEACH_LAT = 45.6134, BEACH_LON = -123.9426;   // Rockaway Beach, Oregon
    private const double CABIN_LAT = 36.2168, CABIN_LON = -81.6746;    // Blue Ridge, North Carolina
    private const double LAKE_LAT = 44.7631, LAKE_LON = -85.6206;      // Traverse City, Michigan

    /// <summary>
    /// Seed the demo data behind the README screenshots.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="imagesRoot"></param>
    /// <returns></returns>
    public static async Task<object> SeedAsync(AppDbContext db, string imagesRoot)
    {
        await DevSeedService.WipeAsync(db);

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var riley = NewUser("riley@example.com", "Riley Chen", isAdmin: true, now);
        var ada = NewUser("ada@example.com", "Ada Whitfield", isAdmin: false, now);
        var marcus = NewUser("marcus@example.com", "Marcus Ellery", isAdmin: false, now);
        var nora = NewUser("nora@example.com", "Nora Beckett", isAdmin: false, now);
        var everyone = new[] { riley, ada, marcus, nora };
        db.Users.AddRange(everyone);

        foreach (var u in everyone)
        {
            db.Allowlist.Add(new AllowlistEntry
            {
                Id = Guid.NewGuid(),
                Email = u.Email,
                AddedBy = riley.Id,
                CreatedAt = now,
            });
        }

        var beach = new Property
        {
            Id = Guid.NewGuid(),
            Name = "Rockaway Beach House",
            Address = "1840 Ocean Terrace, Rockaway Beach, OR 97136",
            WifiNetwork = "SaltAir",
            WifiPassword = "driftwood-2019",
            DoorCode = "4172",
            IcalToken = "demo-beach-ical-token",
            Information = "Firewood is stacked under the deck stairs\nThe outdoor shower runs hot after about a minute\nRecycling goes out Thursday mornings",
            CheckinProcedures = "Park in the gravel pad, not on the street\nLockbox is on the railing to the right of the door\nTurn the water main on under the kitchen sink",
            CheckoutProcedures = "Strip the beds and start a load of towels\nTake the trash and recycling to the bins\nTurn the water main off and lock the slider",
            TimeZone = "America/Los_Angeles",
            CheckInTime = new TimeOnly(16, 0),
            CheckOutTime = new TimeOnly(11, 0),
            IsActive = true,
            Latitude = BEACH_LAT,
            Longitude = BEACH_LON,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var cabin = new Property
        {
            Id = Guid.NewGuid(),
            Name = "Blue Ridge Cabin",
            Address = "77 Laurel Fork Road, Boone, NC 28607",
            WifiNetwork = "RidgeLine",
            WifiPassword = "two-crows-fly",
            DoorCode = "8305",
            IcalToken = "demo-cabin-ical-token",
            Information = "Cell service is thin -- the landline in the kitchen works\nThe woodstove is the fastest way to heat the front room\nBear-proof the trash before dark",
            CheckinProcedures = "Follow the gravel drive past the first cabin\nKeys are in the box on the porch post\nFlip the well pump breaker in the mudroom",
            CheckoutProcedures = "Damp down the woodstove\nLock all the windows\nLeave the thermostat at 55",
            TimeZone = "America/New_York",
            CheckInTime = new TimeOnly(15, 0),
            CheckOutTime = new TimeOnly(10, 0),
            IsActive = true,
            Latitude = CABIN_LAT,
            Longitude = CABIN_LON,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var lake = new Property
        {
            Id = Guid.NewGuid(),
            Name = "Grand Traverse Cottage",
            Address = "412 Birch Point Lane, Traverse City, MI 49686",
            WifiNetwork = "BirchPoint",
            WifiPassword = "cherry-orchard",
            DoorCode = "2664",
            IcalToken = "demo-lake-ical-token",
            Information = "Kayaks and paddles live in the boathouse\nThe dock ladder comes out in October\nGrill propane is in the shed",
            CheckinProcedures = "Gate code is the same as the door code\nBoathouse key hangs by the back door",
            CheckoutProcedures = "Pull the kayaks up past the waterline\nRun the dishwasher\nClose the boathouse",
            TimeZone = "America/Detroit",
            CheckInTime = new TimeOnly(16, 0),
            CheckOutTime = new TimeOnly(10, 0),
            IsActive = true,
            Latitude = LAKE_LAT,
            Longitude = LAKE_LON,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Properties.AddRange(beach, cabin, lake);

        db.FavoriteSpots.AddRange(
            Spot(beach.Id, "Pronto Pup", "602 US-101, Rockaway Beach, OR", "The original corn dog stand. Cash is faster than the card reader.", BEACH_LAT - 0.0062, BEACH_LON + 0.0011, 0, now),
            Spot(beach.Id, "Twin Rocks Viewpoint", "Beach access at NW 3rd Ave, Rockaway Beach, OR", "Walk south at low tide and you can get right up under the arch.", BEACH_LAT - 0.0105, BEACH_LON - 0.0030, 1, now),
            Spot(beach.Id, "Offshore Chowder House", "182 US-101, Rockaway Beach, OR", "Smoked salmon and chowder. Closed Mondays.", BEACH_LAT + 0.0086, BEACH_LON + 0.0016, 2, now),
            Spot(cabin.Id, "Boone Farmers Market", "1450 Blowing Rock Rd, Boone, NC", "Saturday mornings, May through October.", CABIN_LAT - 0.0120, CABIN_LON + 0.0180, 0, now),
            Spot(cabin.Id, "Laurel Fork Falls Trail", "Laurel Fork Rd trailhead, Boone, NC", "Two miles round trip, muddy after rain. Worth it.", CABIN_LAT + 0.0155, CABIN_LON - 0.0090, 1, now),
            Spot(lake.Id, "Birch Point Public Beach", "Birch Point Lane, Traverse City, MI", "Five minute walk. Sandy bottom, no drop-off.", LAKE_LAT + 0.0048, LAKE_LON + 0.0062, 0, now));

        db.WalkingPaths.AddRange(
            new WalkingPath
            {
                Id = Guid.NewGuid(),
                PropertyId = beach.Id,
                Name = "Beach Loop to Twin Rocks",
                Color = "#e07a3f",
                Weight = 4,
                Opacity = 0.9,
                Coordinates = Coords(
                    (BEACH_LAT, BEACH_LON),
                    (BEACH_LAT - 0.0026, BEACH_LON - 0.0024),
                    (BEACH_LAT - 0.0068, BEACH_LON - 0.0034),
                    (BEACH_LAT - 0.0105, BEACH_LON - 0.0030),
                    (BEACH_LAT - 0.0103, BEACH_LON + 0.0010)),
                SortOrder = 0,
                CreatedAt = now,
            },
            new WalkingPath
            {
                Id = Guid.NewGuid(),
                PropertyId = beach.Id,
                Name = "Evening Walk to the Jetty",
                Color = "#3f7fe0",
                Weight = 3,
                Opacity = 0.9,
                Coordinates = Coords(
                    (BEACH_LAT, BEACH_LON),
                    (BEACH_LAT + 0.0048, BEACH_LON - 0.0016),
                    (BEACH_LAT + 0.0092, BEACH_LON - 0.0038)),
                SortOrder = 1,
                CreatedAt = now,
            },
            new WalkingPath
            {
                Id = Guid.NewGuid(),
                PropertyId = cabin.Id,
                Name = "Ridge Trail",
                Color = "#4c9a5a",
                Weight = 4,
                Opacity = 0.9,
                Coordinates = Coords(
                    (CABIN_LAT, CABIN_LON),
                    (CABIN_LAT + 0.0060, CABIN_LON - 0.0030),
                    (CABIN_LAT + 0.0118, CABIN_LON - 0.0068),
                    (CABIN_LAT + 0.0155, CABIN_LON - 0.0090)),
                SortOrder = 0,
                CreatedAt = now,
            });

        db.GuestDoorCodes.AddRange(
            new GuestDoorCode { UserId = ada.Id, PropertyId = beach.Id, DoorCode = "9014" },
            new GuestDoorCode { UserId = marcus.Id, PropertyId = cabin.Id, DoorCode = "3320" });

        db.BlackoutDates.AddRange(
            Blackout(beach.Id, today.AddDays(8), "Deck refinishing", riley.Id, now),
            Blackout(beach.Id, today.AddDays(9), "Deck refinishing", riley.Id, now),
            Blackout(cabin.Id, today.AddDays(26), "Septic service", riley.Id, now));

        var rileyOldest = Stay(riley.Id, beach.Id, today.AddDays(-40), today.AddDays(-36), "Off-season, nobody else on the beach", now.AddDays(-52));
        var rileyPast = Stay(riley.Id, cabin.Id, today.AddDays(-24), today.AddDays(-20), "Long weekend before the season starts", now.AddDays(-30));
        var adaPast = Stay(ada.Id, beach.Id, today.AddDays(-9), today.AddDays(-5), "Kids' spring break", now.AddDays(-40));
        var marcusNow = Stay(marcus.Id, lake.Id, today.AddDays(-3), today.AddDays(1), "Working from the dock", now.AddDays(-14));
        var noraSoon = Stay(nora.Id, cabin.Id, today.AddDays(1), today.AddDays(4), "", now.AddDays(-6));
        var rileySoon = Stay(riley.Id, beach.Id, today.AddDays(2), today.AddDays(6), "Bringing the dog", now.AddDays(-11));
        var marcusLater = Stay(marcus.Id, beach.Id, today.AddDays(10), today.AddDays(14), "Anniversary trip", now.AddDays(-5));
        var adaLater = Stay(ada.Id, cabin.Id, today.AddDays(16), today.AddDays(20), "", now.AddDays(-2));
        var rileyLater = Stay(riley.Id, lake.Id, today.AddDays(22), today.AddDays(26), "Cousins are driving up", now.AddDays(-1));

        db.Reservations.AddRange(
            rileyOldest, rileyPast, adaPast, marcusNow, noraSoon,
            rileySoon, marcusLater, adaLater, rileyLater);
        await db.SaveChangesAsync();

        db.GuestbookEntries.AddRange(
            new GuestbookEntry
            {
                Id = Guid.NewGuid(),
                ReservationId = rileyPast.Id,
                PropertyId = cabin.Id,
                UserId = riley.Id,
                Message = "Woodstove earns its keep. We went through most of the stack -- I left two more bundles on the porch for whoever's next.",
                CreatedAt = now.AddDays(-19),
            },
            new GuestbookEntry
            {
                Id = Guid.NewGuid(),
                ReservationId = adaPast.Id,
                PropertyId = beach.Id,
                UserId = ada.Id,
                Message = "Rained all four days and nobody minded. The kids found a sand dollar the size of a coaster.",
                CreatedAt = now.AddDays(-5),
            });

        await db.SaveChangesAsync();

        WriteGallery(imagesRoot, beach.Id, PaletteFor("beach"));
        WriteGallery(imagesRoot, cabin.Id, PaletteFor("cabin"));
        WriteGallery(imagesRoot, lake.Id, PaletteFor("lake"));

        return new
        {
            adminEmail = riley.Email,
            guestEmail = ada.Email,
            beachId = beach.Id,
            cabinId = cabin.Id,
            lakeId = lake.Id,
        };
    }

    private static User NewUser(string email, string name, bool isAdmin, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        Name = name,
        IsAdmin = isAdmin,
        CalendarToken = Guid.NewGuid().ToString("N"),
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>
    /// Build a demo reservation.
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="propertyId"></param>
    /// <param name="checkIn"></param>
    /// <param name="checkOut"></param>
    /// <param name="notes"></param>
    /// <returns></returns>
    private static Reservation Stay(
        Guid userId, Guid propertyId, DateOnly checkIn, DateOnly checkOut,
        string notes, DateTime createdAt) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PropertyId = propertyId,
            CheckIn = checkIn,
            CheckOut = checkOut,
            Notes = notes,
            CreatedAt = createdAt,
        };

    private static string Coords(params (double Lat, double Lon)[] points) =>
        "[" + string.Join(",", points.Select(p =>
            string.Format(CultureInfo.InvariantCulture, "[{0:0.#####},{1:0.#####}]", p.Lat, p.Lon))) + "]";

    private static Rgba32[][] PaletteFor(string kind) => kind switch
    {
        "beach" =>
        [
            [Hex("#8ec5d6"), Hex("#c3dce2"), Hex("#e8dcc0"), Hex("#d2c09a")],
            [Hex("#4a7c8c"), Hex("#7ba8b5"), Hex("#c9cbb4"), Hex("#a8a184")],
            [Hex("#f0d9b5"), Hex("#e5c79a"), Hex("#9fc0cc"), Hex("#6f96a6")],
        ],
        "cabin" =>
        [
            [Hex("#9fb8a0"), Hex("#6f9070"), Hex("#4a6b4e"), Hex("#33482f")],
            [Hex("#cbd6c0"), Hex("#8a9f7d"), Hex("#5c6f4a"), Hex("#7a6249")],
            [Hex("#5c6f4a"), Hex("#8a7355"), Hex("#b09070"), Hex("#d8c4a4")],
        ],
        _ =>
        [
            [Hex("#a8d5d0"), Hex("#6fb3ae"), Hex("#3f8a86"), Hex("#2a5f5c")],
            [Hex("#e0d3b8"), Hex("#a8c8bd"), Hex("#5f9a95"), Hex("#3b6f6c")],
            [Hex("#cfe3e0"), Hex("#8fbcb6"), Hex("#c4a878"), Hex("#8a7048")],
        ],
    };

    /// <summary>
    /// Write the placeholder gallery for a demo property.
    /// </summary>
    /// <param name="imagesRoot"></param>
    /// <param name="propertyId"></param>
    /// <param name="palettes"></param>
    private static void WriteGallery(string imagesRoot, Guid propertyId, Rgba32[][] palettes)
    {
        var dir = Path.Combine(imagesRoot, propertyId.ToString());
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.jpg"))
        {
            File.Delete(stale);
        }

        var encoder = new JpegEncoder { Quality = 85 };
        string? first = null;
        for (var i = 0; i < palettes.Length; i++)
        {
            var name = $"{i + 1:D3}_{Guid.NewGuid():D}.jpg";
            first ??= name;
            using var image = BandedImage(1600, 1000, palettes[i]);
            image.SaveAsJpeg(Path.Combine(dir, name), encoder);
        }

        if (first is not null)
        {
            ThumbnailService.SetThumbnail(dir, first);
        }
    }

    /// <summary>
    /// Generate a banded placeholder image, so the demo ships no real photos.
    /// </summary>
    /// <param name="width"></param>
    /// <param name="height"></param>
    /// <param name="stops"></param>
    /// <returns></returns>
    private static Image<Rgba32> BandedImage(int width, int height, Rgba32[] stops)
    {
        var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var tilt = (x / (double)width - 0.5) * 0.12;
                    var t = Math.Clamp(y / (double)height + tilt, 0, 1) * (stops.Length - 1);
                    var lower = Math.Min((int)t, stops.Length - 2);
                    row[x] = Blend(stops[lower], stops[lower + 1], t - lower);
                }
            }
        });
        return image;
    }

    private static Rgba32 Blend(Rgba32 a, Rgba32 b, double t) => new(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static Rgba32 Hex(string hex) => Rgba32.ParseHex(hex);
}
