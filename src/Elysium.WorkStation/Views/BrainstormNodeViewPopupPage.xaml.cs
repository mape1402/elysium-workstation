namespace Elysium.WorkStation.Views
{
    public partial class BrainstormNodeViewPopupPage : ContentPage
    {
        private readonly TaskCompletionSource<bool> _resultSource = new();

        public string TitleText { get; }
        public string NodeTitle { get; }
        public string NodeDescription { get; }

        public Task<bool> ResultTask => _resultSource.Task;

        public BrainstormNodeViewPopupPage(string nodeTitle, string nodeDescription)
        {
            TitleText = "Detalle del elemento";
            NodeTitle = string.IsNullOrWhiteSpace(nodeTitle) ? "Sin titulo" : nodeTitle;
            NodeDescription = string.IsNullOrWhiteSpace(nodeDescription) ? "Sin descripcion" : nodeDescription;

            InitializeComponent();
            BindingContext = this;
        }

        protected override void OnDisappearing()
        {
            if (!_resultSource.Task.IsCompleted)
                _resultSource.TrySetResult(false);

            base.OnDisappearing();
        }

        private async void OnCloseClicked(object sender, EventArgs e)
        {
            _resultSource.TrySetResult(true);
            await Navigation.PopModalAsync();
        }
    }
}
