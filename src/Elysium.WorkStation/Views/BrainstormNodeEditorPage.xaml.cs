namespace Elysium.WorkStation.Views
{
    public partial class BrainstormNodeEditorPage : ContentPage
    {
        private readonly TaskCompletionSource<BrainstormNodeEditorResult> _tcs = new();
        private readonly bool _isEditMode;

        public string PageTitle { get; }
        public string HeaderText { get; }
        public string SaveButtonText { get; }
        public string ParentHint { get; }
        public string NodeTitle { get; set; }
        public string NodeDescription { get; set; }

        public Command SaveCommand { get; }
        public Command CancelCommand { get; }

        public BrainstormNodeEditorPage(
            bool isRootLevel,
            bool isEditMode = false,
            string existingTitle = null,
            string existingDescription = null)
        {
            _isEditMode = isEditMode;

            PageTitle = isEditMode
                ? "Editar elemento"
                : isRootLevel ? "Nuevo tema" : "Nueva idea";

            HeaderText = isEditMode
                ? "Editar datos del elemento"
                : isRootLevel ? "Crear nuevo tema" : "Crear nueva idea";

            SaveButtonText = isEditMode ? "Guardar cambios" : "Crear";

            ParentHint = isRootLevel
                ? "Nivel raiz: tema general"
                : "Nivel hijo: idea o subtema";

            NodeTitle = existingTitle ?? string.Empty;
            NodeDescription = existingDescription ?? string.Empty;

            SaveCommand = new Command(OnSave);
            CancelCommand = new Command(async () => await OnCancelAsync());

            InitializeComponent();
            BindingContext = this;
        }

        public Task<BrainstormNodeEditorResult> GetResultAsync() => _tcs.Task;

        private async void OnSave()
        {
            if (string.IsNullOrWhiteSpace(NodeTitle))
            {
                await DisplayAlert(_isEditMode ? "Edicion" : "Captura", "El titulo no puede estar vacio.", "OK");
                return;
            }

            _tcs.TrySetResult(new BrainstormNodeEditorResult(
                NodeTitle.Trim(),
                NodeDescription?.Trim() ?? string.Empty));

            await Navigation.PopModalAsync();
        }

        private async Task OnCancelAsync()
        {
            _tcs.TrySetResult(null);
            await Navigation.PopModalAsync();
        }

        private void OnSubmitByEnter(object sender, EventArgs e)
        {
            if (SaveCommand.CanExecute(null))
                SaveCommand.Execute(null);
        }

        protected override bool OnBackButtonPressed()
        {
            _tcs.TrySetResult(null);
            return base.OnBackButtonPressed();
        }
    }

    public record BrainstormNodeEditorResult(string Title, string Description);
}
