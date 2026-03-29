namespace Elysium.WorkStation.Views
{
    public partial class IgnorePatternPromptPage : ContentPage
    {
        private readonly TaskCompletionSource<string?> _resultSource = new();
        private bool _isClosing;

        public string TitleText { get; }
        public string MessageText { get; }
        public string PlaceholderText { get; }
        public Task<string?> ResultTask => _resultSource.Task;

        public IgnorePatternPromptPage(
            string title,
            string message,
            string placeholder = "*.xml",
            string initialValue = "")
        {
            TitleText = title;
            MessageText = message;
            PlaceholderText = placeholder;

            InitializeComponent();
            BindingContext = this;
            PatternEntry.Text = initialValue;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            MainThread.BeginInvokeOnMainThread(() => PatternEntry.Focus());
        }

        protected override void OnDisappearing()
        {
            if (!_isClosing && !_resultSource.Task.IsCompleted)
            {
                _resultSource.TrySetResult(null);
            }

            base.OnDisappearing();
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            await CloseAsync(null);
        }

        private async void OnAcceptClicked(object sender, EventArgs e)
        {
            await CloseAsync(PatternEntry.Text?.Trim());
        }

        private async Task CloseAsync(string? result)
        {
            if (_isClosing)
            {
                return;
            }

            _isClosing = true;

            try
            {
                if (Navigation.ModalStack.Contains(this))
                {
                    await Navigation.PopModalAsync();
                }
            }
            finally
            {
                if (!_resultSource.Task.IsCompleted)
                {
                    _resultSource.TrySetResult(result);
                }
            }
        }
    }
}
