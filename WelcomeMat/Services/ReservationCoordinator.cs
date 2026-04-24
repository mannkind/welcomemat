using System;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a managed way to create, change, and cancel a stay;
/// validation, persistence, and the notification in one place.
/// </summary>
public class ReservationCoordinator
{
    /// <summary>
    /// Initializes a new instance of the ReservationCoordinator class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="reservationService"></param>
    /// <param name="clock"></param>
    /// <param name="pushover"></param>
    /// <param name="publicUrls"></param>
    public ReservationCoordinator(AppDbContext db, ReservationService reservationService, PropertyClock clock, IPushoverService pushover, PublicUrlService publicUrls)
    {
        this.Db = db;
        this.ReservationService = reservationService;
        this.Clock = clock;
        this.Pushover = pushover;
        this.PublicUrls = publicUrls;
    }

    public record CreateRequest
    {
        public Guid PropertyId { get; init; }

        public DateOnly CheckIn { get; init; }

        public DateOnly CheckOut { get; init; }

        public string? Notes { get; init; }

        public Guid? TargetUserId { get; init; }
    }

    public record ModifyRequest
    {
        public Guid ReservationId { get; init; }

        public DateOnly CheckIn { get; init; }

        public DateOnly CheckOut { get; init; }

        public string? Notes { get; init; }
    }

    /// <summary>
    /// Book a stay.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="actor"></param>
    /// <param name="bypassValidation"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<ReservationMutationResult> CreateAsync(
        CreateRequest request, ClaimsPrincipal actor, bool bypassValidation, CancellationToken ct = default)
    {
        User? reservationOwner = null;

        var result = await this.RunInTransactionAsync(async ct2 =>
        {
            var actorId = ActorId(actor);
            var isAdmin = actor.HasClaim("is_admin", "true");
            var userId = actorId;

            if (isAdmin && request.TargetUserId is { } targetId && targetId != Guid.Empty)
            {
                var targetUser = await this.Db.Users.FindAsync([targetId], ct2);
                if (targetUser is null)
                {
                    return ReservationMutationResult.ValidationError("Target user not found.");
                }

                var allowed = await this.Db.Allowlist.AnyAsync(a => a.Email == targetUser.Email, ct2);
                if (!allowed)
                {
                    return ReservationMutationResult.ValidationError("Target user is not on the allowlist.");
                }

                userId = targetId;
                reservationOwner = targetUser;
            }

            var property = await this.Db.Properties.FindAsync([request.PropertyId], ct2);
            if (property is null || !property.IsActive)
            {
                return ReservationMutationResult.ValidationError("Property not found or inactive.");
            }

            if (!bypassValidation)
            {
                var validation = await this.ReservationService.ValidateAsync(
                    request.CheckIn, request.CheckOut, property, userId, isAdmin, ct: ct2);
                if (!validation.IsValid)
                {
                    return MapValidationFailure(validation.Error!);
                }
            }

            var reservation = new Reservation
            {
                UserId = userId,
                PropertyId = request.PropertyId,
                CheckIn = request.CheckIn,
                CheckOut = request.CheckOut,
                Notes = request.Notes ?? "",
                CreatedAt = DateTime.UtcNow,
            };
            this.Db.Reservations.Add(reservation);
            await this.Db.SaveChangesAsync(ct2);

            return ReservationMutationResult.Success(reservation);
        }, ct);

        if (result.Status == ReservationMutationStatus.Success && result.Reservation is { } created)
        {
            var actorName = actor.FindFirstValue(ClaimTypes.Name) ?? "";
            var msg = reservationOwner is not null
                ? $"Reservation created for {reservationOwner.Name} (by admin {actorName}) for {created.CheckIn:MMM d} → {created.CheckOut:MMM d}."
                : $"Reservation created by {actorName} for {created.CheckIn:MMM d} → {created.CheckOut:MMM d}.";
            await this.Pushover.SendAsync(msg, this.PublicUrls.Build("/reservations"), "View Reservations");
        }

        return result;
    }

    /// <summary>
    /// Change a stay's dates or notes.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="actor"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<ReservationMutationResult> ModifyAsync(
        ModifyRequest request, ClaimsPrincipal actor, CancellationToken ct = default)
    {
        var result = await this.RunInTransactionAsync(async ct2 =>
        {
            var actorId = ActorId(actor);
            var isAdmin = actor.HasClaim("is_admin", "true");

            var r = await this.Db.Reservations
                .Include(x => x.User)
                .Include(x => x.Property)
                .FirstOrDefaultAsync(x => x.Id == request.ReservationId && x.DeletedAt == null, ct2);
            if (r is null)
            {
                return ReservationMutationResult.NotFound;
            }

            if (!isAdmin && r.UserId != actorId)
            {
                return ReservationMutationResult.Forbidden;
            }

            if (!isAdmin && r.CheckOut <= this.Clock.Today(r.Property!.TimeZone))
            {
                return ReservationMutationResult.ValidationError(
                    "Cannot change a reservation that has already ended.");
            }

            var validation = await this.ReservationService.ValidateAsync(
                request.CheckIn, request.CheckOut, r.Property!, r.UserId, isAdmin,
                excludeReservationId: r.Id, ct: ct2);
            if (!validation.IsValid)
            {
                return MapValidationFailure(validation.Error!);
            }

            r.CheckIn = request.CheckIn;
            r.CheckOut = request.CheckOut;
            r.Notes = request.Notes ?? "";
            r.ModifiedAt = DateTime.UtcNow;

            await this.Db.SaveChangesAsync(ct2);
            return ReservationMutationResult.Success(r);
        }, ct);

        if (result.Status == ReservationMutationStatus.Success && result.Reservation is { } r2)
        {
            await this.NotifyModifyOrCancelAsync(r2, actor, "modified");
        }

        return result;
    }

    /// <summary>
    /// Cancel a stay; the row stays and DeletedAt is set.
    /// </summary>
    /// <param name="reservationId"></param>
    /// <param name="actor"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<ReservationMutationResult> CancelAsync(
        Guid reservationId, ClaimsPrincipal actor, CancellationToken ct = default)
    {
        var result = await this.RunInTransactionAsync(async ct2 =>
        {
            var actorId = ActorId(actor);
            var isAdmin = actor.HasClaim("is_admin", "true");

            var r = await this.Db.Reservations
                .Include(x => x.User)
                .Include(x => x.Property)
                .FirstOrDefaultAsync(x => x.Id == reservationId && x.DeletedAt == null, ct2);
            if (r is null)
            {
                return ReservationMutationResult.NotFound;
            }

            if (!isAdmin && r.UserId != actorId)
            {
                return ReservationMutationResult.Forbidden;
            }

            if (!isAdmin && r.CheckOut <= this.Clock.Today(r.Property!.TimeZone))
            {
                return ReservationMutationResult.ValidationError(
                    "Cannot cancel a reservation that has already ended.");
            }

            r.DeletedAt = DateTime.UtcNow;
            await this.Db.SaveChangesAsync(ct2);
            return ReservationMutationResult.Success(r);
        }, ct);

        if (result.Status == ReservationMutationStatus.Success && result.Reservation is { } r2)
        {
            await this.NotifyModifyOrCancelAsync(r2, actor, "cancelled");
        }

        return result;
    }

    /// <summary>
    /// Tell the owners a stay changed.
    /// </summary>
    /// <param name="r"></param>
    /// <param name="actor"></param>
    /// <param name="verb"></param>
    private async Task NotifyModifyOrCancelAsync(Reservation r, ClaimsPrincipal actor, string verb)
    {
        var actorId = ActorId(actor);
        var isAdmin = actor.HasClaim("is_admin", "true");
        var actorName = actor.FindFirstValue(ClaimTypes.Name) ?? "";

        var msg = isAdmin && r.UserId != actorId
            ? $"Reservation {verb} for {r.User?.Name} (by admin {actorName}) for {r.CheckIn:MMM d} → {r.CheckOut:MMM d}."
            : $"Reservation {verb} by {actorName} for {r.CheckIn:MMM d} → {r.CheckOut:MMM d}.";
        await this.Pushover.SendAsync(msg);
    }

    /// <summary>
    /// An overlap is a conflict, not a plain validation failure.
    /// </summary>
    /// <param name="error"></param>
    /// <returns></returns>
    private static ReservationMutationResult MapValidationFailure(string error) =>
        error == ReservationService.OverlapErrorMessage
            ? ReservationMutationResult.Conflict
            : ReservationMutationResult.ValidationError(error);

    /// <summary>
    /// The user id on the principal, or empty.
    /// </summary>
    /// <param name="actor"></param>
    /// <returns></returns>
    private static Guid ActorId(ClaimsPrincipal actor) =>
        Guid.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    /// <summary>
    /// Run the body in an immediate transaction, so two bookings cannot both
    /// pass the overlap check.
    /// </summary>
    /// <param name="body"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    private async Task<ReservationMutationResult> RunInTransactionAsync(
        Func<CancellationToken, Task<ReservationMutationResult>> body, CancellationToken ct)
    {
        SqliteTransaction? rawTransaction = null;
        IDbContextTransaction? efTransaction = null;
        try
        {
            var connection = (SqliteConnection)this.Db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
            }

            rawTransaction = connection.BeginTransaction(deferred: false);
            efTransaction = await this.Db.Database.UseTransactionAsync(rawTransaction, ct);

            var result = await body(ct);

            if (result.Status == ReservationMutationStatus.Success)
            {
                await efTransaction!.CommitAsync(ct);
            }
            else
            {
                await efTransaction!.RollbackAsync(ct);
            }

            return result;
        }
        catch (Exception ex) when (IsBusyException(ex))
        {
            if (efTransaction is not null)
            {
                try { await efTransaction.RollbackAsync(ct); }
                catch { /* the connection may already be unusable; finally disposes it anyway */ }
            }
            return ReservationMutationResult.Conflict;
        }
        finally
        {
            efTransaction?.Dispose();
            rawTransaction?.Dispose();
        }
    }

    private static bool IsBusyException(Exception ex) => ex switch
    {
        SqliteException sqliteEx => IsBusyErrorCode(sqliteEx.SqliteErrorCode),
        DbUpdateException { InnerException: SqliteException inner } => IsBusyErrorCode(inner.SqliteErrorCode),
        _ => false,
    };

    // SQLITE_BUSY = 5, SQLITE_LOCKED = 6.
    private static bool IsBusyErrorCode(int code) => code is 5 or 6;

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The service used to validate the booking rules.
    /// </summary>
    private readonly ReservationService ReservationService;

    /// <summary>
    /// The clock used to read the time in a property's own zone.
    /// </summary>
    private readonly PropertyClock Clock;

    /// <summary>
    /// The service used to notify the owners.
    /// </summary>
    private readonly IPushoverService Pushover;

    /// <summary>
    /// The service used to build externally reachable URLs.
    /// </summary>
    private readonly PublicUrlService PublicUrls;
}
