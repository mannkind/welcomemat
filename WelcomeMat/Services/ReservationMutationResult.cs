using WelcomeMat.Data.Entities;

namespace WelcomeMat.Services;

/// <summary>
/// How a reservation change turned out.
/// </summary>
public enum ReservationMutationStatus
{
    Success,
    ValidationError,
    Forbidden,
    NotFound,
    Conflict,
}

/// <summary>
/// The outcome of a reservation change, and what to tell the guest.
/// </summary>
public record ReservationMutationResult
{
    public required ReservationMutationStatus Status { get; init; }

    public string? Error { get; init; } = null;

    public Reservation? Reservation { get; init; } = null;

    public const string ConflictMessage =
        "Those dates changed while you were booking. Please refresh and try again.";

    /// <summary>
    /// The change went through.
    /// </summary>
    /// <param name="reservation"></param>
    /// <returns></returns>
    public static ReservationMutationResult Success(Reservation reservation) =>
        new() { Status = ReservationMutationStatus.Success, Reservation = reservation };

    /// <summary>
    /// The change broke a booking rule.
    /// </summary>
    /// <param name="error"></param>
    /// <returns></returns>
    public static ReservationMutationResult ValidationError(string error) =>
        new() { Status = ReservationMutationStatus.ValidationError, Error = error };

    public static readonly ReservationMutationResult Forbidden =
        new() { Status = ReservationMutationStatus.Forbidden };

    public static readonly ReservationMutationResult NotFound =
        new() { Status = ReservationMutationStatus.NotFound };

    public static readonly ReservationMutationResult Conflict =
        new() { Status = ReservationMutationStatus.Conflict, Error = ConflictMessage };
}
