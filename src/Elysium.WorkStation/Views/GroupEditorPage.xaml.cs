using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Views
{
    public sealed record GroupEditorResult(string Name, string Description);

    public partial class GroupEditorPage : ContentPage
    {
        private readonly bool _isEditMode;
        private readonly TaskCompletionSource<GroupEditorResult?> _resultSource = new();

        public string PageTitle => _isEditMode ? "Editar grupo" : "Nuevo grupo";
        public string HeaderText => _isEditMode ? "Editar datos del grupo" : "Crear nuevo grupo";
        public string SaveButtonText => _isEditMode ? "Guardar cambios" : "Crear grupo";
        public Task<GroupEditorResult?> ResultTask => _resultSource.Task;

        public GroupEditorPage(VariableGroup existingGroup = null)
        {
            _isEditMode = existingGroup is not null;
            InitializeComponent();
            BindingContext = this;

            if (existingGroup is not null)
            {
                NameEntry.Text = existingGroup.Name;
                DescriptionEditor.Text = existingGroup.Description;
            }
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            _resultSource.TrySetResult(null);
            await Navigation.PopModalAsync();
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            var name = NameEntry.Text?.Trim() ?? string.Empty;
            var description = DescriptionEditor.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                await DisplayAlert("Grupo", "El nombre no puede estar vacío.", "OK");
                return;
            }

            _resultSource.TrySetResult(new GroupEditorResult(name, description));
            await Navigation.PopModalAsync();
        }
    }
}
