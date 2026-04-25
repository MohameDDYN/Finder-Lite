using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Input;
using Finder.Models;
using Finder.Services;
using Newtonsoft.Json;
using Xamarin.Forms;

namespace Finder.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // ── Dependencies ───────────────────────────────────────────────────
        private readonly ILocationService _locationService;
        private readonly string _settingsFilePath;
        private Timer _statusPoller;

        // ── Bindable properties ────────────────────────────────────────────

        private bool _isServiceRunning;
        public bool IsServiceRunning
        {
            get => _isServiceRunning;
            set { _isServiceRunning = value; OnPropertyChanged(); }
        }

        private string _botToken = string.Empty;
        public string BotToken
        {
            get => _botToken;
            set { _botToken = value; OnPropertyChanged(); }
        }

        private string _chatId = string.Empty;
        public string ChatId
        {
            get => _chatId;
            set { _chatId = value; OnPropertyChanged(); }
        }

        private string _statusText = "Service not running";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        // Color is bound directly to BoxView.Color
        private Color _statusDotColor = Color.FromHex("#E53935");
        public Color StatusDotColor
        {
            get => _statusDotColor;
            set { _statusDotColor = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        // ── Events ─────────────────────────────────────────────────────────
        public event EventHandler<string> ShowAlert;
        public event PropertyChangedEventHandler PropertyChanged;

        // ── Commands ───────────────────────────────────────────────────────
        public ICommand StartServiceCommand { get; }
        public ICommand StopServiceCommand { get; }
        public ICommand SaveSettingsCommand { get; }

        // ── Constructor ────────────────────────────────────────────────────
        public MainViewModel()
        {
            _locationService = DependencyService.Get<ILocationService>();
            _settingsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                "secure_settings.json");

            StartServiceCommand = new Command(
                async () => await ExecuteStartAsync(),
                () => !IsBusy && !IsServiceRunning);

            StopServiceCommand = new Command(
                async () => await ExecuteStopAsync(),
                () => !IsBusy && IsServiceRunning);

            SaveSettingsCommand = new Command(ExecuteSave,
                () => !string.IsNullOrWhiteSpace(BotToken) &&
                      !string.IsNullOrWhiteSpace(ChatId));

            LoadSettings();
        }

        // ── Status polling (every 3 s while page is visible) ──────────────

        public void StartStatusPolling()
        {
            _statusPoller?.Dispose();
            _statusPoller = new Timer(_ => PollServiceState(), null, 0, 3_000);
        }

        public void StopStatusPolling()
        {
            _statusPoller?.Change(Timeout.Infinite, Timeout.Infinite);
            _statusPoller?.Dispose();
            _statusPoller = null;
        }

        private void PollServiceState()
        {
            bool running = _locationService?.IsRunning ?? false;

            Device.BeginInvokeOnMainThread(() =>
            {
                if (IsServiceRunning == running) return; // No change — skip UI update

                IsServiceRunning = running;
                StatusText = running
                    ? "Active — listening for Telegram commands"
                    : "Service not running";
                StatusDotColor = running
                    ? Color.FromHex("#4CAF50")
                    : Color.FromHex("#E53935");

                ((Command)StartServiceCommand).ChangeCanExecute();
                ((Command)StopServiceCommand).ChangeCanExecute();
            });
        }

        // ── Command implementations ────────────────────────────────────────

        private async System.Threading.Tasks.Task ExecuteStartAsync()
        {
            if (string.IsNullOrWhiteSpace(BotToken) || string.IsNullOrWhiteSpace(ChatId))
            {
                ShowAlert?.Invoke(this, "Please enter your Bot Token and Chat ID first.");
                return;
            }

            ExecuteSave(); // Persist settings before launching the service

            try
            {
                IsBusy = true;
                RefreshCanExecute();

                await _locationService.StartTracking();

                IsServiceRunning = true;
                StatusText = string.Format("Active since {0:HH:mm:ss}", DateTime.Now);
                StatusDotColor = Color.FromHex("#4CAF50");
            }
            catch (Exception ex)
            {
                ShowAlert?.Invoke(this, "Could not start service: " + ex.Message);
            }
            finally
            {
                IsBusy = false;
                RefreshCanExecute();
            }
        }

        private async System.Threading.Tasks.Task ExecuteStopAsync()
        {
            try
            {
                IsBusy = true;
                RefreshCanExecute();

                await _locationService.StopTracking();

                IsServiceRunning = false;
                StatusText = string.Format("Stopped at {0:HH:mm:ss}", DateTime.Now);
                StatusDotColor = Color.FromHex("#E53935");
            }
            catch (Exception ex)
            {
                ShowAlert?.Invoke(this, "Could not stop service: " + ex.Message);
            }
            finally
            {
                IsBusy = false;
                RefreshCanExecute();
            }
        }

        private void ExecuteSave()
        {
            try
            {
                var settings = new AppSettings
                {
                    BotToken = BotToken?.Trim() ?? string.Empty,
                    ChatId = ChatId?.Trim() ?? string.Empty
                };
                File.WriteAllText(_settingsFilePath,
                    JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch { /* Non-critical — silently swallow */ }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsFilePath)) return;
                var s = JsonConvert.DeserializeObject<AppSettings>(
                    File.ReadAllText(_settingsFilePath));
                if (s == null) return;
                BotToken = s.BotToken ?? string.Empty;
                ChatId = s.ChatId ?? string.Empty;
            }
            catch { }
        }

        private void RefreshCanExecute()
        {
            ((Command)StartServiceCommand).ChangeCanExecute();
            ((Command)StopServiceCommand).ChangeCanExecute();
            ((Command)SaveSettingsCommand).ChangeCanExecute();
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}