namespace Elysium.WorkStation.Views
{
    public partial class PinPromptPage : ContentPage
    {
        private readonly TaskCompletionSource<string?> _resultSource = new();
        private bool _isClosing;

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
            // Fallback: if the modal closes externally, cancel the prompt.
            if (!_isClosing && !_resultSource.Task.IsCompleted)
                _resultSource.TrySetResult(null);

            base.OnDisappearing();
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            await CloseAsync(null);
        }

        private async void OnAcceptClicked(object sender, EventArgs e)
        {
            await CloseAsync(PinEntry.Text?.Trim());
        }

        private async Task CloseAsync(string? result)
        {
            if (_isClosing) return;
            _isClosing = true;

            try
            {
                await Navigation.PopModalAsync();
            }
            finally
            {
                if (!_resultSource.Task.IsCompleted)
                    _resultSource.TrySetResult(result);
            }
        }
    }
}
