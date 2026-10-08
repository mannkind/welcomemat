using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a managed way to stay inside Nominatim's one-call-a-second rule.
/// </summary>
public class GeocodeRateGate : IDisposable
{
    /// <summary>
    /// The most callers allowed to queue before geocoding starts refusing.
    /// </summary>
    public const int MaxQueuedCallers = 1000;

    /// <summary>
    /// Wait for a turn; false once the queue is full.
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<bool> WaitTurnAsync(CancellationToken ct = default)
    {
        using var lease = await this.Limiter.AcquireAsync(1, ct).ConfigureAwait(false);
        return lease.IsAcquired;
    }

    /// <inheritdoc />
    public void Dispose() => this.Limiter.Dispose();

    /// <summary>
    /// Nominatim's usage policy allows one call a second.
    /// </summary>
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The limiter to hold callers to one call per window.
    /// </summary>
    private readonly SlidingWindowRateLimiter Limiter = new(new SlidingWindowRateLimiterOptions
    {
        PermitLimit = 1,
        Window = Window,
        SegmentsPerWindow = 100,
        QueueLimit = MaxQueuedCallers,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true,
    });
}
