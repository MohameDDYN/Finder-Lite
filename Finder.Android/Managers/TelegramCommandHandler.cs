using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Locations;
using Android.OS;
using Android.Preferences;
using Finder.Droid.Services;
using Finder.Models;
using Newtonsoft.Json;
using Xamarin.Forms;
using AndroidLocation = Android.Locations.Location;

namespace Finder.Droid.Managers
{
    /// <summary>
    /// Finder Lite — Telegram long-poll command handler.
    ///
    /// Supported commands:
    ///   /start    — Welcome message
    ///   /help     — List commands
    ///   /location — Fresh GPS fix → sends map pin + coordinate details
    ///   /status   — Service / GPS / battery status
    ///   /version  — Show installed app version
    ///   /update   — Remote APK self-update: /update &lt;version&gt; &lt;url&gt;
    /// </summary>
    public class TelegramCommandHandler
    {
        // ── Constants ──────────────────────────────────────────────────────
        private const string PREF_LAST_UPDATE_ID = "finder_lite_last_update_id";
        private const string PREF_PENDING_VERSION = "finder_lite_pending_version";
        private const int LONG_POLL_TIMEOUT_SEC = 30;
        private const int GPS_FIX_TIMEOUT_MS = 30_000;
        private const int RETRY_DELAY_MS = 5_000;
        private const int STARTUP_DELAY_MS = 2_000;
        // The user has up to 90 s to confirm an update on the device
        private const int UPDATE_CONFIRM_TIMEOUT_MS = 90_000;
        // Wait this long after launching the activity before sending the
        // MessagingCenter event — gives the page time to come to the front
        private const int FOREGROUND_LAUNCH_DELAY_MS = 1_500;

        // ── Fields ─────────────────────────────────────────────────────────
        private readonly Context _context;
        private readonly string _settingsFilePath;
        private readonly HttpClient _httpClient;

        private CancellationTokenSource _cts;
        private long _lastUpdateId;

        // ── Constructor ────────────────────────────────────────────────────

        public TelegramCommandHandler(Context context)
        {
            _context = context;
            _settingsFilePath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.Personal),
                "secure_settings.json");

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(LONG_POLL_TIMEOUT_SEC + 15)
            };

            _lastUpdateId = PreferenceManager
                .GetDefaultSharedPreferences(_context)
                .GetLong(PREF_LAST_UPDATE_ID, 0);
        }

        // ── Public lifecycle ───────────────────────────────────────────────

        public void Start(bool sendStartupMessage = false)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            Task.Run(() => LongPollLoopAsync(_cts.Token));

            if (sendStartupMessage)
                Task.Run(() => SendStartupMessageAsync());
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════════
        // Long-poll loop
        // ══════════════════════════════════════════════════════════════════

        private async Task LongPollLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var settings = LoadSettings();
                    if (settings == null ||
                        string.IsNullOrEmpty(settings.BotToken) ||
                        string.IsNullOrEmpty(settings.ChatId))
                    {
                        await Task.Delay(RETRY_DELAY_MS, ct);
                        continue;
                    }

                    string url =
                        $"https://api.telegram.org/bot{settings.BotToken}/getUpdates" +
                        $"?offset={_lastUpdateId + 1}" +
                        $"&timeout={LONG_POLL_TIMEOUT_SEC}" +
                        $"&limit=10" +
                        $"&allowed_updates=[\"message\"]";

                    var response = await _httpClient.GetAsync(url, ct);
                    string json = await response.Content.ReadAsStringAsync();

                    var result = JsonConvert.DeserializeObject<TgResult>(json);
                    if (result?.Ok != true || result.Updates == null) continue;

                    foreach (var update in result.Updates)
                    {
                        if (update.UpdateId > _lastUpdateId)
                            _lastUpdateId = update.UpdateId;
                        await ProcessUpdateAsync(update, settings);
                    }

                    if (result.Updates.Length > 0)
                        SaveLastUpdateId(_lastUpdateId);
                }
                catch (System.OperationCanceledException) { break; }
                catch
                {
                    try { await Task.Delay(RETRY_DELAY_MS, ct); }
                    catch (System.OperationCanceledException) { break; }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Command routing
        // ══════════════════════════════════════════════════════════════════

        private async Task ProcessUpdateAsync(TgUpdate update, AppSettings settings)
        {
            string raw = update.Message?.Text?.Trim();
            if (string.IsNullOrEmpty(raw)) return;

            string[] parts = raw.Split(new[] { ' ' }, 2);
            string cmd = parts[0].ToLowerInvariant();
            string param = parts.Length > 1 ? parts[1].Trim() : null;

            int at = cmd.IndexOf('@');
            if (at > 0) cmd = cmd.Substring(0, at);

            switch (cmd)
            {
                case "/start": await HandleStartAsync(settings); break;
                case "/help": await HandleHelpAsync(settings); break;
                case "/location": await HandleLocationAsync(settings); break;
                case "/status": await HandleStatusAsync(settings); break;
                case "/version": await HandleVersionAsync(settings); break;
                case "/update": await HandleUpdateAsync(settings, param); break;
                default:
                    await SendMessageAsync(settings,
                        "❓ Unknown command.\nSend /help to see what I can do.");
                    break;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Command handlers — basic
        // ══════════════════════════════════════════════════════════════════

        private async Task HandleStartAsync(AppSettings s)
        {
            await SendMessageAsync(s,
                "👋 *Welcome to Finder Lite!*\n\n" +
                "I send the device's exact GPS location whenever you ask — " +
                "nothing runs in the background otherwise.\n\n" +
                "📍 /location — Get GPS coordinates now\n" +
                "📊 /status  — Check service & battery\n" +
                "❓ /help    — All commands");
        }

        private async Task HandleHelpAsync(AppSettings s)
        {
            await SendMessageAsync(s,
                "📋 *Finder Lite — Commands*\n\n" +
                "/location — Fresh GPS fix with Maps link\n" +
                "/status   — Service status, GPS, battery\n" +
                "/version  — Installed app version\n" +
                "/update   — Self-update: `/update <version> <url>`\n" +
                "/help     — This message\n" +
                "/start    — Welcome & quick-start guide");
        }

        private async Task HandleLocationAsync(AppSettings s)
        {
            await SendMessageAsync(s, "📡 Fetching GPS fix…");

            try
            {
                AndroidLocation loc = GetLastKnownLocation();
                if (loc == null || IsStale(loc))
                    loc = await RequestFreshGpsFixAsync();

                if (loc == null)
                {
                    await SendMessageAsync(s,
                        "❌ *Could not get a GPS fix.*\n" +
                        "Please make sure GPS is enabled and try again.");
                    return;
                }

                await SendLocationPinAsync(s, loc.Latitude, loc.Longitude);

                string lat = loc.Latitude.ToString("F6",
                    System.Globalization.CultureInfo.InvariantCulture);
                string lon = loc.Longitude.ToString("F6",
                    System.Globalization.CultureInfo.InvariantCulture);
                string maps =
                    $"https://www.google.com/maps?q=" +
                    loc.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "," +
                    loc.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);

                int bat = GetBatteryLevel();

                string msg =
                    $"📍 *Current Location*\n\n" +
                    $"Lat: `{lat}`\n" +
                    $"Lng: `{lon}`\n" +
                    (loc.HasAltitude ? $"Alt: {loc.Altitude:F1} m\n" : "") +
                    (loc.HasAccuracy ? $"Accuracy: ±{loc.Accuracy:F0} m\n" : "") +
                    (loc.HasSpeed ? $"Speed: {loc.Speed * 3.6:F1} km/h\n" : "") +
                    (bat >= 0 ? $"Battery: {bat}%\n" : "") +
                    $"\n🗺 [Open in Google Maps]({maps})";

                await SendMessageAsync(s, msg);
            }
            catch (Exception ex)
            {
                await SendMessageAsync(s, $"❌ Location error: {ex.Message}");
            }
        }

        private async Task HandleStatusAsync(AppSettings s)
        {
            bool gps = IsGpsEnabled();
            int bat = GetBatteryLevel();
            string ver = GetAppVersion();

            string msg =
                $"📊 *Finder Lite Status*\n\n" +
                $"Service: ✅ Running\n" +
                $"Version: v{ver}\n" +
                $"GPS:     {(gps ? "✅ Enabled" : "❌ Disabled")}\n" +
                (bat >= 0 ? $"Battery: {bat}%\n" : "") +
                $"Time:    {DateTime.Now:HH:mm:ss}";

            await SendMessageAsync(s, msg);
        }

        private async Task HandleVersionAsync(AppSettings s)
        {
            string ver = GetAppVersion();
            await SendMessageAsync(s,
                $"📦 *Finder Lite*\n\nInstalled version: *v{ver}*");
        }

        // ══════════════════════════════════════════════════════════════════
        // /update command
        // ══════════════════════════════════════════════════════════════════

        private async Task HandleUpdateAsync(AppSettings s, string param)
        {
            if (string.IsNullOrEmpty(param))
            {
                await SendMessageAsync(s,
                    "📦 *Update Command*\n\n" +
                    "Usage: `/update <new_version> <apk_url>`\n\n" +
                    "Example:\n" +
                    "`/update 1.0.1 https://example.com/finder.apk`");
                return;
            }

            string[] args = param.Split(new[] { ' ' }, 2);
            if (args.Length != 2)
            {
                await SendMessageAsync(s,
                    "❌ Invalid format. Use:\n" +
                    "`/update <version> <url>`");
                return;
            }

            string newVersion = args[0].Trim();
            string url = args[1].Trim();

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                await SendMessageAsync(s,
                    "❌ URL must start with `http://` or `https://`.");
                return;
            }

            string currentVersion = GetAppVersion();

            // ── Check install permission (Android 8+) ─────────────────────
            if (!ApkInstaller.CanInstallPackages(_context))
            {
                await SendMessageAsync(s,
                    "⚠️ *Install permission required*\n\n" +
                    "Finder Lite needs permission to install APK updates.\n" +
                    "Opening the Settings page on the device now — please enable " +
                    "*Allow from this source* and run /update again.");

                ApkInstaller.OpenInstallPermissionSettings(_context);
                return;
            }

            // ── Bring app to foreground BEFORE asking for confirmation ───
            // This is the key fix: without it, the dialog is sent into a
            // void if the page isn't visible.
            await SendMessageAsync(s,
                $"📦 *Update Request*\n\n" +
                $"Current: v{currentVersion}\n" +
                $"New: v{newVersion}\n\n" +
                $"📲 Opening Finder Lite on the device — " +
                $"please tap *Yes* on the dialog (90s timeout)…");

            AppLauncher.BringToForeground(_context, "show_update_confirm");

            // Give the activity time to come up before firing the dialog
            await Task.Delay(FOREGROUND_LAUNCH_DELAY_MS);

            // ── Ask the UI for confirmation ──────────────────────────────
            bool confirmed = await RequestUpdateConfirmationAsync(
                currentVersion, newVersion, url);

            if (!confirmed)
            {
                await SendMessageAsync(s,
                    "❌ Update cancelled (declined or timed out).");
                return;
            }

            // ── Download ─────────────────────────────────────────────────
            await SendMessageAsync(s,
                $"⬇️ Downloading APK…\n_This may take a minute._");

            var dl = await ApkDownloaderService.DownloadAsync(_context, url, newVersion);
            if (!dl.Success)
            {
                await SendMessageAsync(s, $"❌ Download failed:\n`{dl.ErrorMessage}`");
                return;
            }

            await SendMessageAsync(s,
                "✅ Download complete and verified.\n" +
                "📲 Launching installer on the device…");

            // ── Save pending flag BEFORE launching installer ─────────────
            SavePendingVersion(newVersion);

            // ── Launch installer ─────────────────────────────────────────
            bool launched = ApkInstaller.InstallApk(_context, dl.FilePath);
            if (!launched)
            {
                ClearPendingVersion();
                await SendMessageAsync(s,
                    "❌ Could not launch the installer. " +
                    "Make sure install permission is granted and try again.");
            }
        }

        /// <summary>
        /// Sends an UpdateConfirmRequest to the UI via MessagingCenter and
        /// awaits the user's Yes/No answer (or a 90 s timeout).
        /// 
        /// Sender type is Application — App.xaml.cs subscribes with the
        /// matching type so the message is actually received.
        /// </summary>
        private async Task<bool> RequestUpdateConfirmationAsync(
            string currentVersion, string newVersion, string url)
        {
            var tcs = new TaskCompletionSource<bool>();

            var request = new UpdateConfirmRequest
            {
                CurrentVersion = currentVersion,
                NewVersion = newVersion,
                DownloadUrl = url,
                ResponseSource = tcs
            };

            // MessagingCenter calls must occur on the main thread.
            // Sender MUST be the Application instance (not 'this') to match
            // the App.xaml.cs subscription signature.
            Device.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var app = Application.Current;
                    if (app == null)
                    {
                        tcs.TrySetResult(false);
                        return;
                    }

                    MessagingCenter.Send<Application, UpdateConfirmRequest>(
                        app, "ShowUpdateConfirm", request);
                }
                catch
                {
                    tcs.TrySetResult(false);
                }
            });

            // Race the confirmation against a 90-second timeout
            var timeout = Task.Delay(UPDATE_CONFIRM_TIMEOUT_MS);
            var done = await Task.WhenAny(tcs.Task, timeout);

            if (done == timeout)
            {
                tcs.TrySetResult(false);
                return false;
            }

            return await tcs.Task;
        }

        // ══════════════════════════════════════════════════════════════════
        // GPS helpers
        // ══════════════════════════════════════════════════════════════════

        private AndroidLocation GetLastKnownLocation()
        {
            try
            {
                var lm = (LocationManager)_context.GetSystemService(Context.LocationService);
                return lm?.GetLastKnownLocation(LocationManager.GpsProvider);
            }
            catch { return null; }
        }

        private bool IsStale(AndroidLocation loc)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.JellyBeanMr1)
            {
                long ageMs =
                    (SystemClock.ElapsedRealtimeNanos() - loc.ElapsedRealtimeNanos)
                    / 1_000_000L;
                return ageMs > 60_000;
            }
            return (Java.Lang.JavaSystem.CurrentTimeMillis() - loc.Time) > 60_000;
        }

        private async Task<AndroidLocation> RequestFreshGpsFixAsync()
        {
            var tcs = new TaskCompletionSource<AndroidLocation>();
            try
            {
                var lm = (LocationManager)_context.GetSystemService(Context.LocationService);
                if (lm == null || !lm.IsProviderEnabled(LocationManager.GpsProvider))
                    return null;

                var listener = new SingleShotLocationListener(tcs);

                var handler = new Handler(Looper.MainLooper);
                handler.Post(() =>
                {
                    try
                    {
                        lm.RequestLocationUpdates(
                            LocationManager.GpsProvider,
                            0L, 0f, listener, Looper.MainLooper);
                    }
                    catch (Exception ex) { tcs.TrySetException(ex); }
                });

                var timeout = Task.Delay(GPS_FIX_TIMEOUT_MS);
                var completed = await Task.WhenAny(tcs.Task, timeout);

                try { lm.RemoveUpdates(listener); } catch { }

                return completed == tcs.Task ? await tcs.Task : null;
            }
            catch { return null; }
        }

        private bool IsGpsEnabled()
        {
            try
            {
                var lm = (LocationManager)_context.GetSystemService(Context.LocationService);
                return lm?.IsProviderEnabled(LocationManager.GpsProvider) == true;
            }
            catch { return false; }
        }

        // ══════════════════════════════════════════════════════════════════
        // Battery / version helpers
        // ══════════════════════════════════════════════════════════════════

        private int GetBatteryLevel()
        {
            try
            {
                var filter = new IntentFilter(Intent.ActionBatteryChanged);
                var status = _context.RegisterReceiver(null, filter);

                int level = status?.GetIntExtra(BatteryManager.ExtraLevel, -1) ?? -1;
                int scale = status?.GetIntExtra(BatteryManager.ExtraScale, -1) ?? -1;

                if (level < 0 || scale <= 0) return -1;
                return (int)(level * 100.0f / scale);
            }
            catch { return -1; }
        }

        private string GetAppVersion()
        {
            try
            {
                var pm = _context.PackageManager;
                var info = pm?.GetPackageInfo(_context.PackageName, 0);
                return info?.VersionName ?? "1.0.0";
            }
            catch { return "1.0.0"; }
        }

        // ══════════════════════════════════════════════════════════════════
        // Pending update helpers
        // ══════════════════════════════════════════════════════════════════

        private void SavePendingVersion(string version)
        {
            try
            {
                PreferenceManager.GetDefaultSharedPreferences(_context)
                    .Edit()
                    .PutString(PREF_PENDING_VERSION, version)
                    .Apply();
            }
            catch { }
        }

        private void ClearPendingVersion()
        {
            try
            {
                PreferenceManager.GetDefaultSharedPreferences(_context)
                    .Edit()
                    .Remove(PREF_PENDING_VERSION)
                    .Apply();
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════════
        // Startup message
        // ══════════════════════════════════════════════════════════════════

        private async Task SendStartupMessageAsync()
        {
            try
            {
                await Task.Delay(STARTUP_DELAY_MS);
                var settings = LoadSettings();
                if (settings == null) return;

                int bat = GetBatteryLevel();
                string ver = GetAppVersion();
                string msg =
                    $"✅ *Finder Lite started*\n\n" +
                    $"The service is running and listening.\n" +
                    $"Version: v{ver}\n" +
                    (bat >= 0 ? $"Battery: {bat}%\n" : "") +
                    $"\nSend /location to get coordinates.";

                await SendMessageAsync(settings, msg);
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════════
        // Telegram API helpers
        // ══════════════════════════════════════════════════════════════════

        private async Task SendMessageAsync(AppSettings s, string text)
        {
            try
            {
                string url =
                    $"https://api.telegram.org/bot{s.BotToken}/sendMessage" +
                    $"?chat_id={s.ChatId}" +
                    $"&text={Uri.EscapeDataString(text)}" +
                    $"&parse_mode=Markdown" +
                    $"&disable_web_page_preview=true";
                await _httpClient.GetAsync(url);
            }
            catch { }
        }

        private async Task SendLocationPinAsync(AppSettings s, double lat, double lon)
        {
            try
            {
                string url =
                    $"https://api.telegram.org/bot{s.BotToken}/sendLocation" +
                    $"?chat_id={s.ChatId}" +
                    $"&latitude={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                    $"&longitude={lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                await _httpClient.GetAsync(url);
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════════
        // Settings helpers
        // ══════════════════════════════════════════════════════════════════

        private AppSettings LoadSettings()
        {
            try
            {
                if (!System.IO.File.Exists(_settingsFilePath)) return null;
                return JsonConvert.DeserializeObject<AppSettings>(
                    System.IO.File.ReadAllText(_settingsFilePath));
            }
            catch { return null; }
        }

        private void SaveLastUpdateId(long id)
        {
            try
            {
                PreferenceManager.GetDefaultSharedPreferences(_context)
                    .Edit()
                    .PutLong(PREF_LAST_UPDATE_ID, id)
                    .Apply();
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════════
        // JSON models
        // ══════════════════════════════════════════════════════════════════

        private class TgResult
        {
            [JsonProperty("ok")] public bool Ok { get; set; }
            [JsonProperty("result")] public TgUpdate[] Updates { get; set; }
        }

        private class TgUpdate
        {
            [JsonProperty("update_id")] public long UpdateId { get; set; }
            [JsonProperty("message")] public TgMessage Message { get; set; }
        }

        private class TgMessage
        {
            [JsonProperty("text")] public string Text { get; set; }
            [JsonProperty("chat")] public TgChat Chat { get; set; }
        }

        private class TgChat
        {
            [JsonProperty("id")] public long Id { get; set; }
        }

        // ══════════════════════════════════════════════════════════════════
        // One-shot GPS listener
        // ══════════════════════════════════════════════════════════════════

        private class SingleShotLocationListener
            : Java.Lang.Object, ILocationListener
        {
            private readonly TaskCompletionSource<AndroidLocation> _tcs;
            private bool _done;

            public SingleShotLocationListener(
                TaskCompletionSource<AndroidLocation> tcs) => _tcs = tcs;

            public void OnLocationChanged(AndroidLocation location)
            {
                if (_done) return;
                _done = true;
                _tcs.TrySetResult(location);
            }

            public void OnProviderDisabled(string provider)
            {
                if (_done) return;
                _done = true;
                _tcs.TrySetResult(null);
            }

            public void OnProviderEnabled(string provider) { }

            public void OnStatusChanged(
                string provider, Availability status, Bundle extras)
            { }
        }
    }
}