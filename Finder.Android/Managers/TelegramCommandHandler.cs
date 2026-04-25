using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Locations;
using Android.OS;
using Android.Preferences;
using Finder.Models;
using Newtonsoft.Json;
using AndroidLocation = Android.Locations.Location;

namespace Finder.Droid.Managers
{
    /// <summary>
    /// Finder Lite — Telegram long-poll command handler.
    ///
    /// Uses Telegram's server-side long-polling (timeout=30 s) so commands
    /// are received with ~1–2 s latency without hammering the API.
    ///
    /// Supported commands:
    ///   /start    — Welcome message
    ///   /help     — List commands
    ///   /location — Fresh GPS fix → sends map pin + coordinate details
    ///   /status   — Service/GPS/battery status
    /// </summary>
    public class TelegramCommandHandler
    {
        // ── Constants ──────────────────────────────────────────────────────
        private const string PREF_LAST_UPDATE_ID = "finder_lite_last_update_id";
        private const int LONG_POLL_TIMEOUT_SEC = 30;
        private const int GPS_FIX_TIMEOUT_MS = 30_000;   // 30 s GPS timeout
        private const int RETRY_DELAY_MS = 5_000;    // retry after error
        private const int STARTUP_DELAY_MS = 2_000;    // let service settle

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

            // Timeout must be > long-poll window to avoid premature cancellation
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(LONG_POLL_TIMEOUT_SEC + 15)
            };

            // Restore the last processed update ID (prevents replaying old commands)
            _lastUpdateId = PreferenceManager
                .GetDefaultSharedPreferences(_context)
                .GetLong(PREF_LAST_UPDATE_ID, 0);
        }

        // ── Public lifecycle ───────────────────────────────────────────────

        /// <summary>
        /// Begins the long-poll loop on a background Task.
        /// Optionally sends a startup message to Telegram.
        /// </summary>
        public void Start(bool sendStartupMessage = false)
        {
            if (_cts != null) return; // Guard against double-start

            _cts = new CancellationTokenSource();
            Task.Run(() => LongPollLoopAsync(_cts.Token));

            if (sendStartupMessage)
                Task.Run(() => SendStartupMessageAsync());
        }

        /// <summary>Cancels the poll loop and releases resources.</summary>
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

                    // If settings are missing, wait and retry
                    if (settings == null ||
                        string.IsNullOrEmpty(settings.BotToken) ||
                        string.IsNullOrEmpty(settings.ChatId))
                    {
                        await Task.Delay(RETRY_DELAY_MS, ct);
                        continue;
                    }

                    // Telegram long-poll: server blocks until a message arrives
                    // or the timeout expires (whichever is first)
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

                    // Persist the highest seen ID after each successful batch
                    if (result.Updates.Length > 0)
                        SaveLastUpdateId(_lastUpdateId);
                }
                catch (OperationCanceledException)
                {
                    break; // Graceful shutdown
                }
                catch
                {
                    // Network error or parse failure — wait before retrying
                    try { await Task.Delay(RETRY_DELAY_MS, ct); }
                    catch (OperationCanceledException) { break; }
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

            // Lower-case and strip optional bot-username suffix (/cmd@BotName)
            string cmd = raw.ToLowerInvariant();
            int at = cmd.IndexOf('@');
            if (at > 0) cmd = cmd.Substring(0, at);

            switch (cmd)
            {
                case "/start":
                    await HandleStartAsync(settings);
                    break;
                case "/help":
                    await HandleHelpAsync(settings);
                    break;
                case "/location":
                    await HandleLocationAsync(settings);
                    break;
                case "/status":
                    await HandleStatusAsync(settings);
                    break;
                default:
                    await SendMessageAsync(settings,
                        "❓ Unknown command.\nSend /help to see what I can do.");
                    break;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Command handlers
        // ══════════════════════════════════════════════════════════════════

        private async Task HandleStartAsync(AppSettings s)
        {
            await SendMessageAsync(s,
                "👋 *Welcome to Finder Lite!*\n\n" +
                "I will send you the device's exact GPS location whenever you ask — " +
                "nothing runs in the background otherwise.\n\n" +
                "📍 /location — Get GPS coordinates now\n" +
                "📊 /status  — Check service & battery\n" +
                "❓ /help    — All commands");
        }

        private async Task HandleHelpAsync(AppSettings s)
        {
            await SendMessageAsync(s,
                "📋 *Finder Lite — Commands*\n\n" +
                "/location — Fresh GPS fix with Google Maps link\n" +
                "/status   — Service status, GPS state, battery\n" +
                "/help     — This message\n" +
                "/start    — Welcome & quick-start guide");
        }

        private async Task HandleLocationAsync(AppSettings s)
        {
            // Acknowledge immediately so the user knows the request is being handled
            await SendMessageAsync(s, "📡 Fetching GPS fix…");

            try
            {
                // 1. Try last-known location first (instant, no battery cost)
                AndroidLocation loc = GetLastKnownLocation();

                // 2. If null or older than 60 s, request a fresh fix
                if (loc == null || IsStale(loc))
                    loc = await RequestFreshGpsFixAsync();

                if (loc == null)
                {
                    await SendMessageAsync(s,
                        "❌ *Could not get a GPS fix.*\n" +
                        "Please make sure GPS is enabled on the device and try again.");
                    return;
                }

                // Send a native Telegram map pin (shows inline in chat)
                await SendLocationPinAsync(s, loc.Latitude, loc.Longitude);

                // Then send a detailed text message
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

            string msg =
                $"📊 *Finder Lite Status*\n\n" +
                $"Service: ✅ Running\n" +
                $"GPS:     {(gps ? "✅ Enabled" : "❌ Disabled")}\n" +
                (bat >= 0 ? $"Battery: {bat}%\n" : "") +
                $"Time:    {DateTime.Now:HH:mm:ss}";

            await SendMessageAsync(s, msg);
        }

        // ══════════════════════════════════════════════════════════════════
        // GPS helpers
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Returns the cached GPS fix from the system, if available.</summary>
        private AndroidLocation GetLastKnownLocation()
        {
            try
            {
                var lm = (LocationManager)_context.GetSystemService(Context.LocationService);
                return lm?.GetLastKnownLocation(LocationManager.GpsProvider);
            }
            catch { return null; }
        }

        /// <summary>Returns true if the location fix is older than 60 seconds.</summary>
        private bool IsStale(AndroidLocation loc)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.JellyBeanMr1)
            {
                // ElapsedRealtimeNanos is monotonic and boot-safe
                long ageMs =
                    (SystemClock.ElapsedRealtimeNanos() - loc.ElapsedRealtimeNanos)
                    / 1_000_000L;
                return ageMs > 60_000;
            }
            // Fallback: wall-clock UTC time comparison
            return (Java.Lang.JavaSystem.CurrentTimeMillis() - loc.Time) > 60_000;
        }

        /// <summary>
        /// Requests a single GPS fix from the hardware.
        /// Waits up to GPS_FIX_TIMEOUT_MS for a result; returns null on timeout.
        /// </summary>
        private async Task<AndroidLocation> RequestFreshGpsFixAsync()
        {
            var tcs = new TaskCompletionSource<AndroidLocation>();

            try
            {
                var lm = (LocationManager)_context.GetSystemService(Context.LocationService);

                if (lm == null || !lm.IsProviderEnabled(LocationManager.GpsProvider))
                    return null;

                var listener = new SingleShotLocationListener(tcs);

                // RequestLocationUpdates requires a Looper — use the main thread's Looper
                var handler = new Handler(Looper.MainLooper);
                handler.Post(() =>
                {
                    try
                    {
                        // Positional args only — named params cause CS1503 ambiguity
                        lm.RequestLocationUpdates(
                            LocationManager.GpsProvider,
                            0L,     // minTimeMs
                            0f,     // minDistanceMeters
                            listener,
                            Looper.MainLooper);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });

                // Race: GPS fix vs 30-second timeout
                var timeout = Task.Delay(GPS_FIX_TIMEOUT_MS);
                var completed = await Task.WhenAny(tcs.Task, timeout);

                // Always remove the listener to stop GPS hardware
                try { lm.RemoveUpdates(listener); }
                catch { }

                return completed == tcs.Task ? await tcs.Task : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Returns true if the device GPS provider is currently enabled.</summary>
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
        // Battery helper
        // ══════════════════════════════════════════════════════════════════

        private int GetBatteryLevel()
        {
            try
            {
                var filter = new Android.Content.IntentFilter(
                    Android.Content.Intent.ActionBatteryChanged);
                var status = _context.RegisterReceiver(null, filter);

                int level = status?.GetIntExtra(
                    Android.OS.BatteryManager.ExtraLevel, -1) ?? -1;
                int scale = status?.GetIntExtra(
                    Android.OS.BatteryManager.ExtraScale, -1) ?? -1;

                if (level < 0 || scale <= 0) return -1;
                return (int)(level * 100.0f / scale);
            }
            catch { return -1; }
        }

        // ══════════════════════════════════════════════════════════════════
        // Startup message
        // ══════════════════════════════════════════════════════════════════

        private async Task SendStartupMessageAsync()
        {
            try
            {
                // Give the service a moment to fully initialise
                await Task.Delay(STARTUP_DELAY_MS);

                var settings = LoadSettings();
                if (settings == null) return;

                int bat = GetBatteryLevel();
                string msg =
                    $"✅ *Finder Lite started*\n\n" +
                    $"The service is now running and listening.\n" +
                    (bat >= 0 ? $"Battery: {bat}%\n" : "") +
                    $"\nSend /location to get your current GPS coordinates.";

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
        // JSON models (internal, not shared)
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

        /// <summary>
        /// ILocationListener that completes the TCS on the first fix,
        /// then becomes inert. The caller is responsible for RemoveUpdates().
        /// </summary>
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

            // Provider disabled before a fix arrived
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