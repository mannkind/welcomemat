using System.Threading.Tasks;

namespace WelcomeMat.Services;

/// <summary>
/// An interface representing a way to push a notification to the owners.
/// </summary>
public interface IPushoverService
{
    /// <summary>
    /// Send a message, optionally with a link back into the application.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="url"></param>
    /// <param name="urlTitle"></param>
    /// <returns></returns>
    Task SendAsync(string message, string? url = null, string? urlTitle = null);
}
