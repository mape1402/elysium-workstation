using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;
using System.Collections.ObjectModel;

namespace Elysium.WorkStation.Views
{
    public partial class KanbanPage : ContentPage
    {
        private readonly IKanbanTaskRepository _repository;
        private readonly IToastService _toastService;

        private KanbanTask? _draggedTask;

        public ObservableCollection<KanbanTask> PendingTasks { get; } = [];
        public ObservableCollection<KanbanTask> InProgressTasks { get; } = [];
        public ObservableCollection<KanbanTask> BlockedTasks { get; } = [];
        public ObservableCollection<KanbanTask> DoneTasks { get; } = [];

        public string CountText
        {
            get
            {
                int total = PendingTasks.Count + InProgressTasks.Count + BlockedTasks.Count + DoneTasks.Count;
                return total == 0
                    ? "Sin tareas"
                    : $"{total} tarea{(total == 1 ? "" : "s")} · ⏳{PendingTasks.Count}  🔄{InProgressTasks.Count}  🚫{BlockedTasks.Count}  ✅{DoneTasks.Count}";
            }
        }

        public Command<string> AddCommand { get; }
        public Command<KanbanTask> EditCommand { get; }
        public Command<KanbanTask> DeleteCommand { get; }

        public KanbanPage(IKanbanTaskRepository repository, IToastService toastService)
        {
            _repository = repository;
            _toastService = toastService;

            AddCommand = new Command<string>(async (s) => await AddTaskAsync(s));
            EditCommand = new Command<KanbanTask>(async (t) => await EditTaskAsync(t));
            DeleteCommand = new Command<KanbanTask>(async (t) => await DeleteTaskAsync(t));

            InitializeComponent();
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadTasksAsync();
        }

        private async Task LoadTasksAsync()
        {
            var all = await _repository.GetAllAsync();

            PendingTasks.Clear();
            InProgressTasks.Clear();
            BlockedTasks.Clear();
            DoneTasks.Clear();

            foreach (var task in all)
            {
                GetCollection(task.Status).Add(task);
            }

            OnPropertyChanged(nameof(CountText));
        }

        private ObservableCollection<KanbanTask> GetCollection(KanbanStatus status) => status switch
        {
            KanbanStatus.Pending    => PendingTasks,
            KanbanStatus.InProgress => InProgressTasks,
            KanbanStatus.Blocked    => BlockedTasks,
            KanbanStatus.Done       => DoneTasks,
            _                       => PendingTasks
        };

        // ── Drag & Drop ──────────────────────────────────────────────

        private void OnDragStarting(object? sender, DragStartingEventArgs e)
        {
            if (sender is GestureRecognizer gr && gr.Parent is VisualElement ve && ve.BindingContext is KanbanTask task)
            {
                _draggedTask = task;
                e.Data.Properties["KanbanTask"] = task;

                if (ve is Border border)
                {
                    border.Opacity = 0.5;
                    border.Scale = 0.95;
                }
            }
        }

        private void OnCardPointerEntered(object? sender, PointerEventArgs e)
        {
            if (sender is Border border && border.Content is Grid grid)
            {
                var del = grid.Children.OfType<Border>().FirstOrDefault();
                if (del is not null) del.FadeTo(1, 150);
            }
        }

        private void OnCardPointerExited(object? sender, PointerEventArgs e)
        {
            if (sender is Border border && border.Content is Grid grid)
            {
                var del = grid.Children.OfType<Border>().FirstOrDefault();
                if (del is not null) del.FadeTo(0, 150);
            }
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;

            if (sender is GestureRecognizer gr && gr.Parent is Border column)
            {
                column.StrokeThickness = 3;
                column.Stroke = new SolidColorBrush(GetColumnAccentColor(column));
            }
        }

        private void OnDragLeave(object? sender, DragEventArgs e)
        {
            if (sender is GestureRecognizer gr && gr.Parent is Border column)
            {
                column.StrokeThickness = 1;
                column.Stroke = Application.Current!.RequestedTheme == AppTheme.Dark
                    ? new SolidColorBrush(Color.FromArgb("#404040"))
                    : new SolidColorBrush(Color.FromArgb("#E0E0E0"));
            }
        }

        private async void OnDropPending(object? sender, DropEventArgs e)
        {
            await HandleDropAsync(sender, KanbanStatus.Pending);
        }

        private async void OnDropInProgress(object? sender, DropEventArgs e)
        {
            await HandleDropAsync(sender, KanbanStatus.InProgress);
        }

        private async void OnDropBlocked(object? sender, DropEventArgs e)
        {
            await HandleDropAsync(sender, KanbanStatus.Blocked);
        }

        private async void OnDropDone(object? sender, DropEventArgs e)
        {
            await HandleDropAsync(sender, KanbanStatus.Done);
        }

        private async Task HandleDropAsync(object? sender, KanbanStatus newStatus)
        {
            ResetColumnStyle(sender);

            var task = _draggedTask;
            _draggedTask = null;

            if (task is null || task.Status == newStatus)
                return;

            await MoveTaskAsync(task, newStatus);
        }

        private void ResetColumnStyle(object? sender)
        {
            if (sender is GestureRecognizer gr && gr.Parent is Border column)
            {
                column.StrokeThickness = 1;
                column.Stroke = Application.Current!.RequestedTheme == AppTheme.Dark
                    ? new SolidColorBrush(Color.FromArgb("#404040"))
                    : new SolidColorBrush(Color.FromArgb("#E0E0E0"));
            }
        }

        private Color GetColumnAccentColor(Border column)
        {
            if (column == ColumnPending)    return Color.FromArgb("#9E9E9E");
            if (column == ColumnInProgress) return Color.FromArgb("#1E88E5");
            if (column == ColumnBlocked)    return Color.FromArgb("#FB8C00");
            if (column == ColumnDone)       return Color.FromArgb("#43A047");
            return Colors.Grey;
        }

        // ── CRUD ─────────────────────────────────────────────────────

        private async Task AddTaskAsync(string? statusParam)
        {
            var status = Enum.TryParse<KanbanStatus>(statusParam, out var parsed)
                ? parsed
                : KanbanStatus.Pending;

            var editor = new KanbanTaskEditorPage(status);
            await Navigation.PushModalAsync(editor);
            var result = await editor.GetResultAsync();

            if (result is null) return;

            var collection = GetCollection(status);

            var task = new KanbanTask
            {
                Title = result.Title,
                Description = result.Description,
                Status = status,
                SortOrder = collection.Count
            };

            await _repository.SaveAsync(task);
            collection.Add(task);
            OnPropertyChanged(nameof(CountText));
            await _toastService.ShowAsync("✅ Tarea creada");
        }

        private async Task EditTaskAsync(KanbanTask task)
        {
            if (task is null) return;

            var editor = new KanbanTaskEditorPage(task.Status, task.Title, task.Description);
            await Navigation.PushModalAsync(editor);
            var result = await editor.GetResultAsync();

            if (result is null) return;

            task.Title = result.Title;
            task.Description = result.Description;
            await _repository.UpdateAsync(task);
            await LoadTasksAsync();
            await _toastService.ShowAsync("💾 Tarea actualizada");
        }

        private async Task DeleteTaskAsync(KanbanTask task)
        {
            if (task is null) return;

            bool confirm = await DisplayAlert(
                "Eliminar tarea",
                $"¿Eliminar \"{task.Title}\"?",
                "Sí",
                "No");

            if (!confirm) return;

            await _repository.DeleteAsync(task.Id);
            GetCollection(task.Status).Remove(task);
            OnPropertyChanged(nameof(CountText));
            await _toastService.ShowAsync("🗑️ Tarea eliminada");
        }

        private async Task MoveTaskAsync(KanbanTask task, KanbanStatus newStatus)
        {
            var oldCollection = GetCollection(task.Status);
            task.Status = newStatus;
            task.SortOrder = GetCollection(newStatus).Count;
            await _repository.UpdateAsync(task);

            oldCollection.Remove(task);
            GetCollection(newStatus).Add(task);
            OnPropertyChanged(nameof(CountText));

            await _toastService.ShowAsync($"Movida a {task.StatusDisplay}");
        }
    }
}
