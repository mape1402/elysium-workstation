using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Elysium.WorkStation.Views
{
    // Priority filter removed — no related types here anymore.

public partial class KanbanPage : ContentPage, INotifyPropertyChanged
    {
        private readonly IKanbanTaskRepository _repository;
        private readonly IToastService _toastService;

        private KanbanTask? _draggedTask;
        private List<KanbanTask> _allTasks = [];
        private readonly Dictionary<Border, CancellationTokenSource> _deleteHideTimers = [];

        public ObservableCollection<KanbanTask> PendingTasks { get; } = [];
        public ObservableCollection<KanbanTask> InProgressTasks { get; } = [];
        public ObservableCollection<KanbanTask> BlockedTasks { get; } = [];
        public ObservableCollection<KanbanTask> DoneTasks { get; } = [];

        // Priority filters removed

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

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

        // Priority filter state removed

        // Priority helper methods removed

        // Priority UI handlers removed

        // Priority overlay handler removed

        public KanbanPage(IKanbanTaskRepository repository, IToastService toastService)
        {
            _repository = repository;
            _toastService = toastService;

            AddCommand = new Command<string>(async (s) => await AddTaskAsync(s));
            EditCommand = new Command<KanbanTask>(async (t) => await EditTaskAsync(t));
            DeleteCommand = new Command<KanbanTask>(async (t) => await DeleteTaskAsync(t));
            // Priority commands removed
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
            _allTasks = await _repository.GetAllAsync();
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var filtered = _allTasks.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var term = SearchText.Trim();
                filtered = filtered.Where(t =>
                    t.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    t.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            // Priority filtering removed — only search filter remains

            PendingTasks.Clear();
            InProgressTasks.Clear();
            BlockedTasks.Clear();
            DoneTasks.Clear();

            foreach (var task in filtered)
                InsertSorted(GetCollection(task.Status), task);

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
            if (sender is Border border)
            {
                var del = FindDeleteOverlay(border);
                if (del is not null)
                    ShowDeleteOverlay(del);
            }
        }

        private void OnCardPointerExited(object? sender, PointerEventArgs e)
        {
            if (sender is Border border)
            {
                var del = FindDeleteOverlay(border);
                if (del is not null)
                    ScheduleHideDeleteOverlay(del);
            }
        }

        private void OnDeleteOverlayPointerEntered(object? sender, PointerEventArgs e)
        {
            if (sender is Border del)
                ShowDeleteOverlay(del);
        }

        private void OnDeleteOverlayPointerExited(object? sender, PointerEventArgs e)
        {
            if (sender is Border del)
                ScheduleHideDeleteOverlay(del);
        }

        private void ShowDeleteOverlay(Border del)
        {
            CancelHideDeleteOverlay(del);
            del.InputTransparent = false;
            _ = del.FadeTo(1, 120);
        }

        private void ScheduleHideDeleteOverlay(Border del)
        {
            CancelHideDeleteOverlay(del);

            var cts = new CancellationTokenSource();
            _deleteHideTimers[del] = cts;
            _ = HideDeleteOverlayAsync(del, cts);
        }

        private async Task HideDeleteOverlayAsync(Border del, CancellationTokenSource cts)
        {
            try
            {
                await Task.Delay(140, cts.Token);
                if (cts.Token.IsCancellationRequested)
                    return;

                await del.FadeTo(0, 120);
                del.InputTransparent = true;
            }
            catch (TaskCanceledException)
            {
            }
            finally
            {
                if (_deleteHideTimers.TryGetValue(del, out var current) && ReferenceEquals(current, cts))
                    _deleteHideTimers.Remove(del);

                cts.Dispose();
            }
        }

        private void CancelHideDeleteOverlay(Border del)
        {
            if (_deleteHideTimers.TryGetValue(del, out var existing))
            {
                existing.Cancel();
                _deleteHideTimers.Remove(del);
            }
        }

        private static Border? FindDeleteOverlay(Border cardBorder)
        {
            if (cardBorder.Parent is Grid container)
            {
                return container.Children
                    .OfType<Border>()
                    .FirstOrDefault(b => !ReferenceEquals(b, cardBorder));
            }

            if (cardBorder.Content is Grid contentGrid)
            {
                return contentGrid.Children.OfType<Border>().FirstOrDefault();
            }

            return null;
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
                column.Stroke = new SolidColorBrush(GetColumnBaseStrokeColor(column));
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
                column.Stroke = new SolidColorBrush(GetColumnBaseStrokeColor(column));
            }
        }

        private Color GetColumnBaseStrokeColor(Border column)
        {
            bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

            if (column == ColumnPending)
                return Color.FromArgb(isDark ? "#69778F" : "#D5D5D5");
            if (column == ColumnInProgress)
                return Color.FromArgb(isDark ? "#2D77C8" : "#B3D4FC");
            if (column == ColumnBlocked)
                return Color.FromArgb(isDark ? "#D17A26" : "#FFCC80");
            if (column == ColumnDone)
                return Color.FromArgb(isDark ? "#3E9E57" : "#A5D6A7");

            return Color.FromArgb(isDark ? "#404040" : "#E0E0E0");
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

            var task = new KanbanTask
            {
                Title = result.Title,
                Description = result.Description,
                Status = status,
                Priority = result.Priority,
                SortOrder = GetCollection(status).Count
            };

            await _repository.SaveAsync(task);
            _allTasks.Add(task);
            ApplyFilters();
            await _toastService.ShowAsync("✅ Tarea creada");
        }

        private async Task EditTaskAsync(KanbanTask task)
        {
            if (task is null) return;

            var editor = new KanbanTaskEditorPage(task.Status, task.Title, task.Description, task.Priority);
            await Navigation.PushModalAsync(editor);
            var result = await editor.GetResultAsync();

            if (result is null) return;

            task.Title = result.Title;
            task.Description = result.Description;
            task.Priority = result.Priority;
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
            _allTasks.Remove(task);
            ApplyFilters();
            await _toastService.ShowAsync("🗑️ Tarea eliminada");
        }

        private async Task MoveTaskAsync(KanbanTask task, KanbanStatus newStatus)
        {
            task.Status = newStatus;
            task.CompletedOn = newStatus == KanbanStatus.Done ? DateTime.Now : null;
            task.SortOrder = GetCollection(newStatus).Count;
            await _repository.UpdateAsync(task);
            ApplyFilters();

            await _toastService.ShowAsync($"Movida a {task.StatusDisplay}");
        }

        private static void InsertSorted(ObservableCollection<KanbanTask> collection, KanbanTask task)
        {
            int index = 0;
            while (index < collection.Count && collection[index].Priority >= task.Priority)
                index++;
            collection.Insert(index, task);
        }
    }
}
