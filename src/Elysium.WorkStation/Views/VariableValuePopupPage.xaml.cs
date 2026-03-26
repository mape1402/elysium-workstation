namespace Elysium.WorkStation.Views
{
    public partial class VariableValuePopupPage : ContentPage
    {
        private readonly TaskCompletionSource<bool> _resultSource = new();
        private string _copyButtonText = "Copiar";

        public string TitleText { get; }
        public string VariableName { get; }
        public string VariableDescription { get; }
        public string VariableValue { get; }
        public string CopyButtonText
        {
            get => _copyButtonText;
            set
            {
                _copyButtonText = value;
                OnPropertyChanged();
            }
        }
        public Task<bool> ResultTask => _resultSource.Task;

        public VariableValuePopupPage(string variableName, string variableDescription, string variableValue)
        {
            TitleText = "Valor de variable";
            VariableName = variableName;
            VariableDescription = string.IsNullOrWhiteSpace(variableDescription)
                ? "Sin descripcion."
                : variableDescription;
            VariableValue = variableValue;

            InitializeComponent();
            BindingContext = this;
        }

        protected override void OnDisappearing()
        {
            if (!_resultSource.Task.IsCompleted)
                _resultSource.TrySetResult(false);

            base.OnDisappearing();
        }

        private async void OnAcceptClicked(object sender, EventArgs e)
        {
            _resultSource.TrySetResult(true);
            await Navigation.PopModalAsync();
        }

        private async void OnCopyClicked(object sender, EventArgs e)
        {
            await Clipboard.Default.SetTextAsync(VariableValue ?? string.Empty);
            CopyButtonText = "Copiado";
        }
    }
}
