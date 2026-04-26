using System.Threading.Tasks;

namespace Finder.Models
{
    /// <summary>
    /// Carries update confirmation data from TelegramCommandHandler
    /// to the UI layer (MainPage) via MessagingCenter.
    ///
    /// The TaskCompletionSource lets TelegramCommandHandler await the
    /// user's Yes/No answer before starting the download.
    /// </summary>
    public class UpdateConfirmRequest
    {
        /// <summary>Current installed version (e.g. "1.0.0")</summary>
        public string CurrentVersion { get; set; }

        /// <summary>Requested new version (e.g. "1.0.1")</summary>
        public string NewVersion { get; set; }

        /// <summary>Direct download URL to the new APK file.</summary>
        public string DownloadUrl { get; set; }

        /// <summary>
        /// Set to true (confirmed) or false (declined / timeout) by MainPage
        /// after the user taps Yes or No.
        /// TelegramCommandHandler awaits this before proceeding.
        /// </summary>
        public TaskCompletionSource<bool> ResponseSource { get; set; }
    }
}