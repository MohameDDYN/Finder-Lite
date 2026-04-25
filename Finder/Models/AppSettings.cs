namespace Finder.Models
{
    /// <summary>
    /// Telegram bot configuration — persisted as JSON in the app's personal folder.
    /// Path: /data/data/com.finderlite/files/secure_settings.json
    /// </summary>
    public class AppSettings
    {
        public string BotToken { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty;
    }
}