using System.Threading.Tasks;

namespace Finder.Services
{
    /// <summary>
    /// Platform-agnostic contract for controlling the background service.
    /// The Android implementation is registered via [assembly: Dependency(...)].
    /// </summary>
    public interface ILocationService
    {
        /// <summary>True while BackgroundLocationService is running.</summary>
        bool IsRunning { get; }

        /// <summary>Starts the foreground service.</summary>
        Task StartTracking();

        /// <summary>Stops the foreground service (user-requested).</summary>
        Task StopTracking();
    }
}