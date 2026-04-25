using System.Collections.Generic;
using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Xamarin.Forms;
using Xamarin.Forms.Platform.Android;

namespace Finder.Droid
{
    [Activity(
        Label = "Finder Lite",
        Icon = "@mipmap/ic_launcher",
        Theme = "@style/MainTheme.Splash",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation
    )]
    public class MainActivity : FormsAppCompatActivity
    {
        private const int REQUEST_PERMISSIONS = 1001;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            // Switch away from the splash theme before rendering content
            SetTheme(Resource.Style.MainTheme);

            base.OnCreate(savedInstanceState);
            Forms.Init(this, savedInstanceState);
            LoadApplication(new App());

            RequestRequiredPermissions();
        }

        // ── Permission handling ────────────────────────────────────────────

        private void RequestRequiredPermissions()
        {
            var toRequest = new List<string>();

            // Location permissions — needed for GPS
            if (CheckSelfPermission(Manifest.Permission.AccessFineLocation)
                    != Permission.Granted)
                toRequest.Add(Manifest.Permission.AccessFineLocation);

            if (CheckSelfPermission(Manifest.Permission.AccessCoarseLocation)
                    != Permission.Granted)
                toRequest.Add(Manifest.Permission.AccessCoarseLocation);

            // Notification permission — required on Android 13+ (API 33)
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                if (CheckSelfPermission(Manifest.Permission.PostNotifications)
                        != Permission.Granted)
                    toRequest.Add(Manifest.Permission.PostNotifications);
            }

            if (toRequest.Count > 0)
                RequestPermissions(toRequest.ToArray(), REQUEST_PERMISSIONS);
        }

        public override void OnRequestPermissionsResult(
            int requestCode,
            string[] permissions,
            [GeneratedEnum] Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode != REQUEST_PERMISSIONS) return;

            // Check if location was denied and notify the UI
            for (int i = 0; i < permissions.Length; i++)
            {
                if ((permissions[i] == Manifest.Permission.AccessFineLocation ||
                     permissions[i] == Manifest.Permission.AccessCoarseLocation) &&
                    grantResults[i] != Permission.Granted)
                {
                    MessagingCenter.Send<MainActivity, string>(
                        this,
                        "PermissionDenied",
                        "Location permission was denied. GPS will not work.");
                    return;
                }
            }
        }
    }
}