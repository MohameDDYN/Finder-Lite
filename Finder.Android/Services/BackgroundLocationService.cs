using Android.App;
using Android.Content;
using Android.OS;
using Android.Preferences;
using Finder.Droid.Managers;
using JavaSystem = Java.Lang.JavaSystem;

namespace Finder.Droid.Services
{
    /// <summary>
    /// Finder Lite — Minimal foreground service.
    ///
    /// Responsibilities:
    ///   • Keep the process alive as a foreground service (persistent notification)
    ///   • Own a TelegramCommandHandler instance that long-polls for /location,
    ///     /status, /help, /version, /update, /start commands
    ///   • Auto-restart in multiple scenarios:
    ///       - Process killed by OS (StartCommandResult.Sticky)
    ///       - Service crash (OnDestroy fallback)
    ///       - Task removed / app swiped from recents (OnTaskRemoved)
    ///       - Device reboot (BootReceiver reads PREF_WAS_RUNNING)
    ///       - APK self-update (PackageReplacedReceiver)
    ///
    /// What this service does NOT do:
    ///   • No continuous GPS updates — GPS is requested on-demand per /location
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

        // ── Restart-after-task-removed alarm ───────────────────────────────
        private const int RESTART_ALARM_REQUEST = 9001;
        private const int RESTART_DELAY_MS = 1_000; // 1 s

        // ── Core component ─────────────────────────────────────────────────
        private TelegramCommandHandler _commandHandler;

        public override IBinder OnBind(Intent intent) => null;

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        public override StartCommandResult OnStartCommand(
            Intent intent, StartCommandFlags flags, int startId)
        {
            IsRunning = true;
            IsStoppingByUserRequest = false;

            // Persist the "running" flag so BootReceiver knows whether to
            // restart the service after a device reboot
            SaveWasRunning(true);

            // Foreground notification (required to keep the service alive)
            CreateNotificationChannel();
            StartForeground(SERVICE_NOTIFICATION_ID,
                BuildNotification(
                    "Finder Lite is active",
                    "Listening for Telegram commands…"));

            // Initialise command handler once per service instance
            if (_commandHandler == null)
            {
                bool explicitStart =
                    intent?.GetBooleanExtra("explicit_user_start", false) ?? false;

                _commandHandler = new TelegramCommandHandler(this);
                _commandHandler.Start(sendStartupMessage: explicitStart);
            }

            // Sticky → Android recreates the service if it gets killed
            return StartCommandResult.Sticky;
        }

        /// <summary>
        /// Called when the user swipes the app away from the recents list.
        /// On many OEMs, this kills the foreground service too. We schedule
        /// an alarm to restart the service shortly after.
        /// </summary>
        public override void OnTaskRemoved(Intent rootIntent)
        {
            base.OnTaskRemoved(rootIntent);

            // If the user explicitly stopped, do not restart
            if (IsStoppingByUserRequest) return;

            ScheduleRestartAlarm();
        }

        public override void OnDestroy()
        {
            IsRunning = false;
            base.OnDestroy();

            // Stop the command handler cleanly
            try
            {
                _commandHandler?.Stop();
                _commandHandler = null;
            }
            catch { }

            // ── User-requested stop → respect it ───────────────────────────
            if (IsStoppingByUserRequest)
            {
                SaveWasRunning(false);
                IsStoppingByUserRequest = false;
                return;
            }

            // ── Otherwise → attempt restart ────────────────────────────────
            // We try a direct StartForegroundService first; if that gets
            // throttled by Android's background restrictions, the alarm
            // scheduled below will recover us a moment later.
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

            // Belt-and-braces: also schedule an alarm in case the direct
            // start fails (very common on Android 8+ background restrictions)
            ScheduleRestartAlarm();
        }

        // ══════════════════════════════════════════════════════════════════
        // Restart alarm — recovers the service when OnDestroy can't directly
        // restart it (Android 8+ background-service restrictions).
        // ══════════════════════════════════════════════════════════════════

        private void ScheduleRestartAlarm()
        {
            try
            {
                var serviceIntent = new Intent(
                    ApplicationContext, typeof(BackgroundLocationService));
                serviceIntent.PutExtra("explicit_user_start", false);

                var pendingFlags = Build.VERSION.SdkInt >= BuildVersionCodes.M
                    ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
                    : PendingIntentFlags.UpdateCurrent;

                // GetForegroundService() ensures the alarm correctly starts
                // a foreground-eligible service on API 26+. On older APIs we
                // fall back to GetService() which works the same.
                PendingIntent pi;
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    pi = PendingIntent.GetForegroundService(
                        ApplicationContext,
                        RESTART_ALARM_REQUEST,
                        serviceIntent,
                        pendingFlags);
                }
                else
                {
                    pi = PendingIntent.GetService(
                        ApplicationContext,
                        RESTART_ALARM_REQUEST,
                        serviceIntent,
                        pendingFlags);
                }

                var alarmManager = (AlarmManager)GetSystemService(AlarmService);
                long triggerAt = JavaSystem.CurrentTimeMillis() + RESTART_DELAY_MS;

                // SetExact (not SetExactAndAllowWhileIdle) — no Doze bypass needed
                // for a 1-second delay
                alarmManager?.SetExact(AlarmType.RtcWakeup, triggerAt, pi);
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════════
        // Persistence — survives reboot
        // ══════════════════════════════════════════════════════════════════

        private void SaveWasRunning(bool running)
        {
            try
            {
                PreferenceManager.GetDefaultSharedPreferences(this)
                    .Edit()
                    .PutBoolean(BootReceiver.PREF_WAS_RUNNING, running)
                    .Apply();
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════════
        // Notification helpers
        // ══════════════════════════════════════════════════════════════════

        private Notification BuildNotification(string title, string body)
        {
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
                .SetSmallIcon(Resource.Mipmap.icon)
                .SetContentIntent(pendingIntent)
                .SetOngoing(true);

            // Pre-O devices use legacy priority API
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