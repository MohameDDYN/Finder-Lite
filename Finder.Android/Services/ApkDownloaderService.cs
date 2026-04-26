using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Android.Content;

namespace Finder.Droid.Services
{
    /// <summary>
    /// Result wrapper for APK downloads.
    /// (async methods cannot have 'out' parameters in any C# version,
    /// so we return a struct-like wrapper instead.)
    /// </summary>
    public class ApkDownloadResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Downloads an APK file from a direct URL into the app's cache directory.
    ///
    /// Features:
    ///   • Uses CookieContainer (required for Google Drive redirect-based downloads)
    ///   • Follows HTTP redirects automatically
    ///   • Validates the APK magic bytes ("PK\x03\x04") before declaring success —
    ///     this rejects HTML error pages that some hosts return on bad URLs
    /// </summary>
    public static class ApkDownloaderService
    {
        // APK files are ZIP archives — first 4 bytes are always: 50 4B 03 04
        private static readonly byte[] APK_MAGIC = { 0x50, 0x4B, 0x03, 0x04 };

        /// <summary>
        /// Downloads the APK from <paramref name="url"/> to the cache directory.
        /// Returns an <see cref="ApkDownloadResult"/> describing the outcome.
        /// </summary>
        public static async Task<ApkDownloadResult> DownloadAsync(
            Context context, string url, string version)
        {
            var result = new ApkDownloadResult();

            // ── Build cache path ──────────────────────────────────────────
            string fileName = $"finder_lite_{version}.apk";
            string cacheDir = context.CacheDir.AbsolutePath;
            string apkPath = Path.Combine(cacheDir, fileName);

            // Remove any leftover from a previous attempt
            try { if (File.Exists(apkPath)) File.Delete(apkPath); }
            catch { }

            // ── Build HTTP client (with cookies + redirect following) ────
            var cookies = new CookieContainer();
            var handler = new HttpClientHandler
            {
                CookieContainer = cookies,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };

            // Some hosts compress responses — let HttpClient decompress them
            if (handler.SupportsAutomaticDecompression)
                handler.AutomaticDecompression =
                    DecompressionMethods.GZip | DecompressionMethods.Deflate;

            HttpClient http = null;
            try
            {
                http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };

                // Mimic a browser to bypass bot-protection on simple hosts
                http.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Linux; Android) FinderLite/1.0");

                // ── Download to disk in a streaming fashion ─────────────
                using (var response = await http.GetAsync(
                           url, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        result.ErrorMessage =
                            $"HTTP {(int)response.StatusCode} — {response.ReasonPhrase}";
                        return result;
                    }

                    using (var src = await response.Content.ReadAsStreamAsync())
                    using (var dst = new FileStream(apkPath,
                               FileMode.Create, FileAccess.Write, FileShare.None,
                               81920, true))
                    {
                        await src.CopyToAsync(dst);
                    }
                }

                // ── Validate APK magic bytes ─────────────────────────────
                if (!IsValidApk(apkPath))
                {
                    try { File.Delete(apkPath); } catch { }
                    result.ErrorMessage =
                        "Downloaded file is not a valid APK " +
                        "(magic bytes mismatch — was the URL correct?).";
                    return result;
                }

                // ── Success ──────────────────────────────────────────────
                result.Success = true;
                result.FilePath = apkPath;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Download failed: {ex.Message}";
                try { if (File.Exists(apkPath)) File.Delete(apkPath); } catch { }
                return result;
            }
            finally
            {
                http?.Dispose();
            }
        }

        /// <summary>
        /// Returns true if the file's first 4 bytes match the ZIP/APK magic
        /// number (0x50 0x4B 0x03 0x04).
        /// </summary>
        private static bool IsValidApk(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < 4) return false;
                    var head = new byte[4];
                    fs.Read(head, 0, 4);
                    for (int i = 0; i < 4; i++)
                        if (head[i] != APK_MAGIC[i]) return false;
                    return true;
                }
            }
            catch { return false; }
        }
    }
}