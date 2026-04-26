using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Preferences;
using Android.Runtime;
using Finder.Models;
using Newtonsoft.Json;
using Xamarin.Forms;
using Xamarin.Forms.Platform.Android;

namespace Finder.Droid
{
    [Activity(
        Label = "Finder Lite",
        Icon = "@mipmap/icon",
        Theme = "@style/MainTheme.Splash",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation
    )]
    public class MainActivity : FormsAppCompatActivity
    {
        private const int REQUEST_PERMISSIONS = 1001;
        private const string PREF_PENDING_VERSION = "finder_lite_pending_version";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            SetTheme(Resource.Style.MainTheme);

            base.OnCreate(savedInstanceState);
            Forms.Init(this, savedInstanceState);
            LoadApplication(new App());

            RequestRequiredPermissions();
        }

        protected override void OnResume()
        {
            base.OnResume();

            // Detect a successful update install: if a "pending version" was
            // saved before the installer was launched and the currently running
            // version now matches it, the update succeeded.
            CheckPendingUpdate();
        }

        // ══════════════════════════════════════════════════════════════════
        // Pending update detection
        // ══════════════════════════════════════════════════════════════════

        private void CheckPendingUpdate()
        {
            try
            {
                var prefs = PreferenceManager.GetDefaultSharedPreferences(this);
                string pending = prefs.GetString(PREF_PENDING_VERSION, null);
                if (string.IsNullOrEmpty(pending)) return;

                string current = GetCurrentVersion();

                // Always clear the flag — whether we succeeded or not, we don't
                // want to keep firing this notification on every resume
                prefs.Edit().Remove(PREF_PENDING_VERSION).Apply();

                if (string.Equals(current, pending, StringComparison.OrdinalIgnoreCase))
                {
                    // Update succeeded — let Telegram know
                    Task.Run(() => SendUpdateSuccessAsync(current));
                }
            }
            catch { }
        }

        private string GetCurrentVersion()
        {
            try
            {
                var info = PackageManager?.GetPackageInfo(PackageName, 0);
                return info?.VersionName ?? "1.0.0";
            }
            catch { return "1.0.0"; }
        }

        private async Task SendUpdateSuccessAsync(string version)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.Personal),
                    "secure_settings.json");
                if (!System.IO.File.Exists(path)) return;

                var s = JsonConvert.DeserializeObject<AppSettings>(
                    System.IO.File.ReadAllText(path));
                if (s == null ||
                    string.IsNullOrEmpty(s.BotToken) ||
                    string.IsNullOrEmpty(s.ChatId)) return;

                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    string text =
                        $"✅ *Update Complete*\n\n" +
                        $"Finder Lite is now running v{version}.";
                    string url =
                        $"https://api.telegram.org/bot{s.BotToken}/sendMessage" +
                        $"?chat_id={s.ChatId}" +
                        $"&text={Uri.EscapeDataString(text)}" +
                        $"&parse_mode=Markdown";
                    await http.GetAsync(url);
                }
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════════
        // Runtime permissions
        // ══════════════════════════════════════════════════════════════════

        private void RequestRequiredPermissions()
        {
            var toRequest = new List<string>();

            if (CheckSelfPermission(Manifest.Permission.AccessFineLocation)
                    != Permission.Granted)
                toRequest.Add(Manifest.Permission.AccessFineLocation);

            if (CheckSelfPermission(Manifest.Permission.AccessCoarseLocation)
                    != Permission.Granted)
                toRequest.Add(Manifest.Permission.AccessCoarseLocation);

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