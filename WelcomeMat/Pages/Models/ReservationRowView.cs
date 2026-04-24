using WelcomeMat.Data.Entities;

namespace WelcomeMat.Pages.Models;

/// <summary>
/// One row of the reservations timeline. Upcoming and past rows differ only in
/// whether the change and cancel buttons show, so they render from one shape.
/// </summary>
public record ReservationRowView
{
    public required Reservation Reservation { get; init; }

    public string EditUrl { get; init; } = string.Empty;

    public bool ShowChangeAndCancel { get; init; }

    public bool CanGuestbook { get; init; }

    public string GuestbookLabel { get; init; } = string.Empty;

    public string GuestbookMessage { get; init; } = string.Empty;
}
