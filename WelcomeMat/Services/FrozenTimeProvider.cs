using System;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a clock stopped at a fixed instant.
/// </summary>
public class FrozenTimeProvider : TimeProvider
{
    /// <summary>
    /// Initializes a new instance of the FrozenTimeProvider class.
    /// </summary>
    /// <param name="utcNow"></param>
    public FrozenTimeProvider(DateTimeOffset utcNow)
    {
        this.UtcNow = utcNow;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => this.UtcNow;

    /// <summary>
    /// The instant this clock always reports.
    /// </summary>
    private readonly DateTimeOffset UtcNow;
}
