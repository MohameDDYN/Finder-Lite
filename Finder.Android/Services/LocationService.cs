using System;
using System.Threading.Tasks;
using Android.Content;
using Android.OS;
using Android.Preferences;
using Finder.Droid.Services;
using Finder.Services;
using Xamarin.Forms;
using Application = Android.App.Application;

[assembly: Dependency(typeof(LocationService))]
namespace Finder.Droid.Services
{
    /// <summary>
    /// Android implementation of ILocationService.
    /// Starts and stops BackgroundLocationService as a foreground service.
    /// </summary>
    public class LocationService : ILocationService
    {
        private readonly Context _context;

        public LocationService()
        {
            _context = Application.Context;
        }

        /// <inheritdoc/>
        public bool IsRunning => BackgroundLocationService.IsRunning;

        /// <inheritdoc/>
        public Task StartTracking()
        {
            try
            {
                BackgroundLocationService.IsStoppingByUserRequest = false;

                // Persist "running" flag for boot recovery
                SetWasRunning(true);

                var intent = new Intent(_context, typeof(BackgroundLocationService));
                intent.PutExtra("explicit_user_start", true);

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                    _context.StartForegroundService(intent);
                else
                    _context.StartService(intent);

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        /// <inheritdoc/>
        public Task StopTracking()
        {
            try
            {
                // Set BEFORE StopService() so OnDestroy() skips auto-restart
                BackgroundLocationService.IsStoppingByUserRequest = true;

                // Clear "running" flag — we don't want boot recovery to start
                // a service the user explicitly stopped
                SetWasRunning(false);

                var intent = new Intent(_context, typeof(BackgroundLocationService));
                _context.StopService(intent);

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        // ── SharedPreferences helper ───────────────────────────────────────

        private void SetWasRunning(bool value)
        {
            try
            {
                PreferenceManager.GetDefaultSharedPreferences(_context)
                    .Edit()
                    .PutBoolean(BootReceiver.PREF_WAS_RUNNING, value)
                    .Apply();
            }
            catch { }
        }
    }
}