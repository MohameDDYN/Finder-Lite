using Finder.Models;
using Finder.ViewModels;
using Xamarin.Forms;

namespace Finder.Views
{
    public partial class MainPage : ContentPage
    {
        private readonly MainViewModel _viewModel;

        public MainPage()
        {
            InitializeComponent();

            _viewModel = new MainViewModel();
            _viewModel.ShowAlert += async (sender, message) =>
                await DisplayAlert("Finder Lite", message, "OK");

            BindingContext = _viewModel;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Status polling every 3 s while the page is visible
            _viewModel.StartStatusPolling();

            // Subscribe to update-confirmation requests from TelegramCommandHandler.
            // Subscriber type is 'object' so we don't depend on Android-only types.
            MessagingCenter.Subscribe<object, UpdateConfirmRequest>(
                this, "ShowUpdateConfirm", OnUpdateConfirmRequested);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _viewModel.StopStatusPolling();

            MessagingCenter.Unsubscribe<object, UpdateConfirmRequest>(
                this, "ShowUpdateConfirm");
        }

        // ── Update confirmation dialog ─────────────────────────────────────

        private async void OnUpdateConfirmRequested(
            object sender, UpdateConfirmRequest request)
        {
            if (request?.ResponseSource == null) return;

            try
            {
                bool confirmed = await DisplayAlert(
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
                // If anything goes wrong, treat as decline so the handler
                // doesn't hang indefinitely
                request.ResponseSource.TrySetResult(false);
            }
        }
    }
}