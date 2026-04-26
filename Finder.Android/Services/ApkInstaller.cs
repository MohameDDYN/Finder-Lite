using System;
using Android.Content;
using Android.OS;
using Android.Provider;
using AndroidX.Core.Content;

namespace Finder.Droid.Services
{
    /// <summary>
    /// Helpers for the APK install flow:
    ///   • Detect whether the user has granted "Install Unknown Apps"
    ///   • Open the Settings screen to grant it
    ///   • Hand the downloaded APK to the system installer via FileProvider
    /// </summary>
    public static class ApkInstaller
    {
        /// <summary>
        /// True if the system will allow this app to install APKs.
        /// On Android 7 and below, this permission was granted at install time
        /// and is always available.
        /// </summary>
        public static bool CanInstallPackages(Context context)
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.O) return true;
                return context.PackageManager?.CanRequestPackageInstalls() ?? false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Opens the "Install unknown apps" Settings screen for this app
        /// so the user can grant the permission.
        /// </summary>
        public static void OpenInstallPermissionSettings(Context context)
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

                var intent = new Intent(
                    Settings.ActionManageUnknownAppSources,
                    Android.Net.Uri.Parse("package:" + context.PackageName));

                intent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
            }
            catch { }
        }

        /// <summary>
        /// Launches the system Package Installer for the given APK file.
        /// On API 24+ this requires a content:// Uri produced by FileProvider.
        /// </summary>
        public static bool InstallApk(Context context, string apkPath)
        {
            try
            {
                var file = new Java.IO.File(apkPath);
                if (!file.Exists()) return false;

                Android.Net.Uri apkUri;
                if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
                {
                    // API 24+: a content:// URI from FileProvider is required
                    string authority = context.PackageName + ".fileprovider";
                    apkUri = FileProvider.GetUriForFile(context, authority, file);
                }
                else
                {
                    // API 21–23: file:// URIs still work
                    apkUri = Android.Net.Uri.FromFile(file);
                }

                var intent = new Intent(Intent.ActionView);
                intent.SetDataAndType(apkUri, "application/vnd.android.package-archive");

                // Required so the installer can read the file
                intent.AddFlags(ActivityFlags.GrantReadUriPermission);
                intent.AddFlags(ActivityFlags.NewTask);

                context.StartActivity(intent);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}