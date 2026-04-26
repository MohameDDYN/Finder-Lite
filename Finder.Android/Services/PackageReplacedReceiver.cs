using Android.App;
using Android.Content;
using Android.OS;

namespace Finder.Droid.Services
{
    /// <summary>
    /// Fires when this app's package is replaced (i.e. an update completes).
    /// We use it to restart BackgroundLocationService so the bot keeps
    /// answering Telegram commands without the user having to reopen the app.
    ///
    /// Registered in AndroidManifest.xml with:
    ///   action=android.intent.action.MY_PACKAGE_REPLACED
    /// </summary>
    [BroadcastReceiver(
        Enabled = true,
        Exported = true,
        Name = "com.finderlite.PackageReplacedReceiver")]
    [IntentFilter(new[] { Intent.ActionMyPackageReplaced })]
    public class PackageReplacedReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            if (intent?.Action != Intent.ActionMyPackageReplaced) return;

            try
            {
                var serviceIntent = new Intent(context, typeof(BackgroundLocationService));
                serviceIntent.PutExtra("explicit_user_start", false);

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                    context.StartForegroundService(serviceIntent);
                else
                    context.StartService(serviceIntent);
            }
            catch { /* Silent — there's nothing useful we can do here */ }
        }
    }
}