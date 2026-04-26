using Android.Content;
using Android.OS;

namespace Finder.Droid.Services
{
    /// <summary>
    /// Brings MainActivity to the foreground from a background service or
    /// broadcast receiver. Used by the /update flow so the confirmation
    /// dialog actually appears when the app isn't currently visible.
    /// </summary>
    public static class AppLauncher
    {
        /// <summary>
        /// Launches MainActivity. If the activity is already running it is
        /// brought to the front; otherwise it is started fresh.
        ///
        /// Note: starting an activity from the background is restricted on
        /// Android 10+. To work reliably, this method should be called from
        /// a foreground service context (which Finder Lite already uses).
        /// </summary>
        public static void BringToForeground(Context context, string action = null)
        {
            try
            {
                var intent = new Intent(context, typeof(MainActivity));
                intent.AddFlags(
                    ActivityFlags.NewTask |
                    ActivityFlags.SingleTop |
                    ActivityFlags.ReorderToFront);

                if (!string.IsNullOrEmpty(action))
                    intent.PutExtra("trigger_action", action);

                context.StartActivity(intent);
            }
            catch { /* Best-effort — Android 10+ may block this */ }
        }
    }
}