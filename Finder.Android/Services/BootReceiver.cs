using Android.App;
using Android.Content;
using Android.OS;
using Android.Preferences;

namespace Finder.Droid.Services
{
    /// <summary>
    /// Restarts BackgroundLocationService after the device finishes booting.
    ///
    /// We only restart if the user previously had the service running before
    /// the reboot — we don't auto-start on first boot after install.
    /// This is tracked via the PREF_WAS_RUNNING flag in SharedPreferences.
    /// </summary>
    [BroadcastReceiver(
        Enabled = true,
        Exported = true,
        Name = "com.finderlite.BootReceiver",
        Permission = "android.permission.RECEIVE_BOOT_COMPLETED")]
    [IntentFilter(new[]
    {
        Intent.ActionBootCompleted,
        // Some OEMs (Xiaomi, Huawei) use a non-standard "quick boot" action
        "android.intent.action.QUICKBOOT_POWERON",
        "com.htc.intent.action.QUICKBOOT_POWERON"
    })]
    public class BootReceiver : BroadcastReceiver
    {
        public const string PREF_WAS_RUNNING = "finder_lite_was_running";

        public override void OnReceive(Context context, Intent intent)
        {
            try
            {
                // Only restart if the user had the service running when
                // the device went down — respects "Stop" actions
                var prefs = PreferenceManager.GetDefaultSharedPreferences(context);
                bool wasRunning = prefs.GetBoolean(PREF_WAS_RUNNING, false);
                if (!wasRunning) return;

                var serviceIntent = new Intent(context, typeof(BackgroundLocationService));
                serviceIntent.PutExtra("explicit_user_start", false);

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                    context.StartForegroundService(serviceIntent);
                else
                    context.StartService(serviceIntent);
            }
            catch { /* Best effort — nothing useful to do on failure */ }
        }
    }
}