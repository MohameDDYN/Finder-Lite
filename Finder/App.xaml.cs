using Finder.Models;
using Finder.Views;
using Xamarin.Forms;

namespace Finder
{
    public partial class App : Application
    {
        // ── Public flag set by MainActivity when the activity is in the
        //    foreground. Used by the dialog handler to decide whether the
        //    DisplayAlert can actually be shown right now.
        public static bool IsActivityVisible { get; set; }

        public App()
        {
            InitializeComponent();

            MainPage = new NavigationPage(new MainPage())
            {
                BarBackgroundColor = Color.FromHex("#1565C0"),
                BarTextColor = Color.White
            };

            // ── Subscribe at App level so the listener is alive for the
            //    entire app process lifetime, not just while MainPage is
            //    visible. We use the static Application instance as sender,
            //    matching the sender type used in TelegramCommandHandler.
            MessagingCenter.Subscribe<Application, UpdateConfirmRequest>(
                this, "ShowUpdateConfirm", OnUpdateConfirmRequested);
        }

        protected override void OnStart() { }
        protected override void OnSleep() { IsActivityVisible = false; }
        protected override void OnResume() { IsActivityVisible = true; }

        // ──────────────────────────────────────────────────────────────────
        // Update confirmation handler
        // ──────────────────────────────────────────────────────────────────

        private async void OnUpdateConfirmRequested(
            Application sender, UpdateConfirmRequest request)
        {
            if (request?.ResponseSource == null) return;

            try
            {
                // The MainPage / NavigationPage is what actually owns
                // DisplayAlert — call through Application.Current.MainPage
                var page = Current?.MainPage;
                if (page == null)
                {
                    request.ResponseSource.TrySetResult(false);
                    return;
                }

                bool confirmed = await page.DisplayAlert(
                    "Update Available",
                    $"A new version is available.\n\n" +
                    $"Current: v{request.CurrentVersion}\n" +
                    $"New:     v{request.NewVersion}\n\n" +
                    $"Download and install now?",
                    "Yes, Update",
                    "No");

                request.ResponseSource.TrySetResult(confirmed);
            }
            catch
            {
                // Anything goes wrong → treat as decline so handler doesn't hang
                request.ResponseSource.TrySetResult(false);
            }
        }
    }
}