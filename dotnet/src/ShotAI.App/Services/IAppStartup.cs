namespace ShotAI.App.Services;

/// <summary>
/// A singleton that starts once the main window is shown (spec 11 7.10 rule 3, ARCHITECTURE 4.2
/// step 9): the App calls <see cref="Start"/> on each, in registration order.
/// </summary>
public interface IAppStartup
{
    /// <summary>Subscribes to what the singleton follows; it does no IO.</summary>
    void Start();
}
