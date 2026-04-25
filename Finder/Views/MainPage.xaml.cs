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

            // Wire alert events back to the page (must be on UI thread)
            _viewModel.ShowAlert += async (sender, message) =>
                await DisplayAlert("Finder Lite", message, "OK");

            BindingContext = _viewModel;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            // Poll every 3 s while the page is visible to keep status accurate
            _viewModel.StartStatusPolling();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _viewModel.StopStatusPolling();
        }
    }
}