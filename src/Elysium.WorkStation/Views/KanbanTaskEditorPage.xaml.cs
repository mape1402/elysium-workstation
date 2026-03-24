using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Views
{
    public partial class KanbanTaskEditorPage : ContentPage
    {
        private readonly TaskCompletionSource<KanbanTaskEditorResult?> _tcs = new();

        public string PageTitle { get; }
        public string TaskTitle { get; set; }
        public string TaskDescription { get; set; }
        public KanbanStatus Status { get; }

        public string StatusBadgeText => Status switch
        {
            KanbanStatus.Pending    => "⏳ Pendiente",
            KanbanStatus.InProgress => "🔄 En Progreso",
            KanbanStatus.Blocked    => "⚠️ Bloqueado",
            KanbanStatus.Done       => "✅ Terminado",
            _                       => Status.ToString()
        };

        public Color StatusBadgeColor => Status switch
        {
            KanbanStatus.Pending    => Color.FromArgb("#9E9E9E"),
            KanbanStatus.InProgress => Color.FromArgb("#1E88E5"),
            KanbanStatus.Blocked    => Color.FromArgb("#FB8C00"),
            KanbanStatus.Done       => Color.FromArgb("#43A047"),
            _                       => Colors.Grey
        };

        public Command SaveCommand { get; }
        public Command CancelCommand { get; }

        public KanbanTaskEditorPage(KanbanStatus status, string? existingTitle = null, string? existingDescription = null)
        {
            Status = status;
            PageTitle = existingTitle is null ? "Nueva tarea" : "Editar tarea";
            TaskTitle = existingTitle ?? string.Empty;
            TaskDescription = existingDescription ?? string.Empty;

            SaveCommand = new Command(OnSave);
            CancelCommand = new Command(async () => await OnCancel());

            InitializeComponent();
            BindingContext = this;
        }

        public Task<KanbanTaskEditorResult?> GetResultAsync() => _tcs.Task;

        private async void OnSave()
        {
            if (string.IsNullOrWhiteSpace(TaskTitle))
            {
                await DisplayAlert("Campo requerido", "El título no puede estar vacío.", "OK");
                return;
            }

            _tcs.TrySetResult(new KanbanTaskEditorResult(TaskTitle.Trim(), TaskDescription?.Trim() ?? string.Empty));
            await Navigation.PopModalAsync();
        }

        private async Task OnCancel()
        {
            _tcs.TrySetResult(null);
            await Navigation.PopModalAsync();
        }

        protected override bool OnBackButtonPressed()
        {
            _tcs.TrySetResult(null);
            return base.OnBackButtonPressed();
        }
    }

    public record KanbanTaskEditorResult(string Title, string Description);
}
