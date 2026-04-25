using Android.App;
using Android.Content;
using Android.OS;
using Finder.Droid.Managers;

namespace Finder.Droid.Services
{
    /// <summary>
    /// Finder Lite — Minimal foreground service.
    ///
    /// Responsibilities:
    ///   • Keep the process alive as a foreground service (persistent notification)
    ///   • Own a TelegramCommandHandler instance that long-polls for /location,
    ///     /status, /help and /start commands
    ///   • Auto-restart on crash (unless the user explicitly stopped the service)
    ///
    /// What this service does NOT do:
    ///   • No continuous GPS updates — GPS is requested on-demand per /location
    ///   • No timers, no periodic sends, no GeoJSON recording
    /// </summary>
    [Service(
        Name = "com.finderlite.BackgroundLocationService",
        Enabled = true,
        Exported = false)]
    public class BackgroundLocationService : Service
    {
        // ── Public state (read by LocationService.IsRunning) ───────────────
        public static bool IsRunning = false;
        public static bool IsStoppingByUserRequest = false;

        // ── Notification constants ─────────────────────────────────────────
        private const int SERVICE_NOTIFICATION_ID = 2001;
        private const string CHANNEL_ID = "finder_lite_service";
        private const string CHANNEL_NAME = "Finder Lite Service";

        // ── Core component ─────────────────────────────────────────────────
        private TelegramCommandHandler _commandHandler;

        public override IBinder OnBind(Intent intent) => null;

        // ── Lifecycle ──────────────────────────────────────────────────────

        public override StartCommandResult OnStartCommand(
            Intent intent, StartCommandFlags flags, int startId)
        {
            IsRunning = true;
            IsStoppingByUserRequest = false;

            // Show the persistent notification (required for foreground services)
            CreateNotificationChannel();
            StartForeground(SERVICE_NOTIFICATION_ID,
                BuildNotification(
                    "Finder Lite is active",
                    "Listening for Telegram commands…"));

            // Start command handler only once per service instance
            if (_commandHandler == null)
            {
                bool explicitStart =
                    intent?.GetBooleanExtra("explicit_user_start", false) ?? false;

                _commandHandler = new TelegramCommandHandler(this);
                _commandHandler.Start(sendStartupMessage: explicitStart);
            }

            // Sticky: Android restarts this service if it is killed
            return StartCommandResult.Sticky;
        }

        public override void OnDestroy()
        {
            IsRunning = false;
            base.OnDestroy();

            // Stop and discard the command handler
            try
            {
                _commandHandler?.Stop();
                _commandHandler = null;
            }
            catch { /* Suppress any cleanup errors */ }

            // Auto-restart unless the user explicitly pressed Stop
            if (!IsStoppingByUserRequest)
            {
                try
                {
                    var restartIntent = new Intent(
                        ApplicationContext, typeof(BackgroundLocationService));

                    if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                        StartForegroundService(restartIntent);
                    else
                        StartService(restartIntent);
                }
                catch { }
            }

            // Always reset the flag so a subsequent Start works correctly
            IsStoppingByUserRequest = false;
        }

        // ── Notification helpers ───────────────────────────────────────────

        private Notification BuildNotification(string title, string body)
        {
            // Tapping the notification opens MainActivity
            var pendingFlags = Build.VERSION.SdkInt >= BuildVersionCodes.M
                ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
                : PendingIntentFlags.UpdateCurrent;

            var pendingIntent = PendingIntent.GetActivity(
                this, 0,
                new Intent(this, typeof(MainActivity)),
                pendingFlags);

            var builder = new Notification.Builder(this, CHANNEL_ID)
                .SetContentTitle(title)
                .SetContentText(body)
                .SetSmallIcon(Resource.Drawable.ic_notification)
                .SetContentIntent(pendingIntent)
                .SetOngoing(true);

            // API < 26 does not use channels — set legacy priority directly
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            {
#pragma warning disable CS0618
                builder.SetPriority((int)NotificationPriority.Low);
#pragma warning restore CS0618
            }

            return builder.Build();
        }

        private void CreateNotificationChannel()
        {
            // Notification channels were introduced in Android 8.0 (API 26)
            if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

            var channel = new NotificationChannel(
                CHANNEL_ID,
                CHANNEL_NAME,
                NotificationImportance.Low)
            {
                Description = "Finder Lite background service notification"
            };
            channel.SetShowBadge(false);

            var manager = (NotificationManager)GetSystemService(NotificationService);
            manager?.CreateNotificationChannel(channel);
        }
    }
}