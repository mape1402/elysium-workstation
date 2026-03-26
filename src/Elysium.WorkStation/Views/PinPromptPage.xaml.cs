namespace Elysium.WorkStation.Views
{
    public partial class PinPromptPage : ContentPage
    {
        private readonly TaskCompletionSource<string?> _resultSource = new();

        public string TitleText { get; }
        public string MessageText { get; }
        public string PlaceholderText { get; }
        public Task<string?> ResultTask => _resultSource.Task;

        public PinPromptPage(string title, string message, string placeholder = "PIN")
        {
            TitleText = title;
            MessageText = message;
            PlaceholderText = placeholder;

            InitializeComponent();
            BindingContext = this;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            MainThread.BeginInvokeOnMainThread(() => PinEntry.Focus());
        }

        protected override void OnDisappearing()
        {
            if (!_resultSource.Task.IsCompleted)
                _resultSource.TrySetResult(null);

            base.OnDisappearing();
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            _resultSource.TrySetResult(null);
            await Navigation.PopModalAsync();
        }

        private async void OnAcceptClicked(object sender, EventArgs e)
        {
            _resultSource.TrySetResult(PinEntry.Text?.Trim());
            await Navigation.PopModalAsync();
        }
    }
}
