using System;
using System.Threading.Tasks;
using Android.Content;
using Android.OS;
using Finder.Services;
using Xamarin.Forms;
using Application = Android.App.Application;

// Registers this class with Xamarin's DependencyService
[assembly: Dependency(typeof(Finder.Droid.Services.LocationService))]

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
                // Reset the stop flag before starting
                BackgroundLocationService.IsStoppingByUserRequest = false;

                var intent = new Intent(_context, typeof(BackgroundLocationService));
                intent.PutExtra("explicit_user_start", true);

                // Use StartForegroundService on API 26+ (Oreo and later)
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
                // Flag must be set BEFORE StopService() so OnDestroy() skips auto-restart
                BackgroundLocationService.IsStoppingByUserRequest = true;

                var intent = new Intent(_context, typeof(BackgroundLocationService));
                _context.StopService(intent);

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }
    }
}