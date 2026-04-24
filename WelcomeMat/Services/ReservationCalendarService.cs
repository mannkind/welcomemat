using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WelcomeMat.Data.Entities;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a way to turn reservations into calendar events.
/// </summary>
public class ReservationCalendarService
{
    /// <summary>
    /// Initializes a new instance of the ReservationCalendarService class.
    /// </summary>
    /// <param name="stayTimes"></param>
    /// <param name="clock"></param>
    public ReservationCalendarService(StayTimesService stayTimes, PropertyClock clock)
    {
        this.StayTimes = stayTimes;
        this.Clock = clock;
    }

    /// <summary>
    /// Turn reservations into calendar events, at each property's own check-in
    /// and check-out times.
    /// </summary>
    /// <param name="reservations"></param>
    /// <param name="includeGuest"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<IReadOnlyList<IcalEvent>> BuildEventsAsync(
        IReadOnlyList<Reservation> reservations, bool includeGuest, CancellationToken ct = default)
    {
        var global = await this.StayTimes.GetGlobalAsync(ct);

        return [.. reservations.Select(r => this.BuildEvent(r, includeGuest, global))];
    }

    /// <summary>
    /// Turn one reservation into a calendar event.
    /// </summary>
    /// <param name="r"></param>
    /// <param name="includeGuest"></param>
    /// <param name="global"></param>
    /// <returns></returns>
    private IcalEvent BuildEvent(Reservation r, bool includeGuest, StayTimes global)
    {
        var propertyName = r.Property?.Name ?? "Reservation";
        var summary = includeGuest && r.User is not null
            ? $"{propertyName} — {r.User.Name}"
            : propertyName;

        var timeZone = r.Property?.TimeZone ?? "UTC";
        var times = StayTimesService.Resolve(r.Property?.CheckInTime, r.Property?.CheckOutTime, global);

        return new IcalEvent
        {
            Uid = $"reservation-{r.Id}@welcomemat",
            Start = this.Clock.AtBoundary(r.CheckIn, timeZone, times.CheckIn),
            End = this.Clock.AtBoundary(r.CheckOut, timeZone, times.CheckOut),
            Summary = summary,
            Description = r.Notes,
            Location = r.Property?.Address,
        };
    }

    /// <summary>
    /// The service used to read the configured check-in and check-out times.
    /// </summary>
    private readonly StayTimesService StayTimes;

    /// <summary>
    /// The clock used to read the time in a property's own zone.
    /// </summary>
    private readonly PropertyClock Clock;
}
