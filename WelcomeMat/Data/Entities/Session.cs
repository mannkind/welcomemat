using System;

namespace WelcomeMat.Data.Entities;

/// <summary>
/// A signed-in session, as the cookie ticket store keeps it.
/// </summary>
public class Session
{
    public required string Token { get; set; }
    public required byte[] Data { get; set; }

    /// <summary>
    /// When the session dies, as fractional unix seconds.
    /// </summary>
    public double Expiry { get; set; }
    public Guid? UserId { get; set; }
    public long SecurityVersion { get; set; }

    /// <summary>
    /// When the session was issued, as fractional unix seconds.
    /// </summary>
    public double IssuedAt { get; set; }

    public User? User { get; set; }
}
