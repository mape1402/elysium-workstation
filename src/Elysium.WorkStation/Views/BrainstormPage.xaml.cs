using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;
using Microsoft.Maui.ApplicationModel;
using System.Collections.ObjectModel;
using System.Threading;

namespace Elysium.WorkStation.Views
{
    [QueryProperty(nameof(ParentIdQuery), "parentId")]
    [QueryProperty(nameof(ParentTitleQuery), "parentTitle")]
    public partial class BrainstormPage : ContentPage
    {
        private readonly IBrainstormNodeRepository _repository;
        private readonly IToastService _toastService;

        private int? _currentParentId;
        private string _currentParentTitle = string.Empty;
        private CancellationTokenSource? _breadcrumbScrollCts;
        private bool _isFabPointerInside;

        public ObservableCollection<BrainstormNode> Nodes { get; } = [];
        public ObservableCollection<BrainstormBreadcrumbItem> Breadcrumbs { get; } = [];

        public string CountText => Nodes.Count == 0
            ? "Sin elementos"
            : $"{Nodes.Count} elemento{(Nodes.Count == 1 ? string.Empty : "s")}";

        public string ContextText => _currentParentId is null
            ? "Mapa mental: temas generales"
            : string.IsNullOrWhiteSpace(_currentParentTitle)
                ? "Mapa mental: subideas"
                : $"Tema actual: {_currentParentTitle}";

        public string EmptyText => _currentParentId is null
            ? "Agrega tu primer tema general."
            : "No hay ideas relacionadas.";

        public string AddLabel => _currentParentId is null ? "+ Tema" : "+ Idea";

        public string ParentIdQuery
        {
            set
            {
                if (int.TryParse(value, out int parsed))
                {
                    _currentParentId = parsed;
                }
                else
                {
                    _currentParentId = null;
                    _currentParentTitle = string.Empty;
                }

                RefreshHeaderBindings();
            }
        }

        public string ParentTitleQuery
        {
            set
            {
                _currentParentTitle = Uri.UnescapeDataString(value ?? string.Empty);
                OnPropertyChanged(nameof(ContextText));
            }
        }

        public Command AddCommand { get; }
        public Command BackCommand { get; }
        public Command GoMenuCommand { get; }
        public Command<BrainstormNode> OpenChildrenCommand { get; }
        public Command<BrainstormBreadcrumbItem> NavigatePathCommand { get; }
        public Command<BrainstormNode> ViewCommand { get; }
        public Command<BrainstormNode> EditCommand { get; }
        public Command<BrainstormNode> DeleteCommand { get; }

        public BrainstormPage(IBrainstormNodeRepository repository, IToastService toastService)
        {
            _repository = repository;
            _toastService = toastService;

            AddCommand = new Command(async () => await AddNodeAsync());
            BackCommand = new Command(async () => await GoBackAsync());
            GoMenuCommand = new Command(async () => await GoMenuAsync());
            OpenChildrenCommand = new Command<BrainstormNode>(async (node) => await OpenChildrenAsync(node));
            NavigatePathCommand = new Command<BrainstormBreadcrumbItem>(async (item) => await NavigatePathAsync(item));
            ViewCommand = new Command<BrainstormNode>(async (node) => await ViewNodeAsync(node));
            EditCommand = new Command<BrainstormNode>(async (node) => await EditNodeAsync(node));
            DeleteCommand = new Command<BrainstormNode>(async (node) => await DeleteNodeAsync(node));

            InitializeComponent();
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await EnsureParentContextAsync();
            await LoadBreadcrumbsAsync();
            await LoadNodesAsync();
        }

        private async Task EnsureParentContextAsync()
        {
            if (_currentParentId is not int parentId || !string.IsNullOrWhiteSpace(_currentParentTitle))
                return;

            var parent = await _repository.GetByIdAsync(parentId);
            if (parent is null) return;

            _currentParentTitle = parent.Title;
            OnPropertyChanged(nameof(ContextText));
        }

        private async Task LoadBreadcrumbsAsync()
        {
            var pathNodes = await _repository.GetPathAsync(_currentParentId);
            Breadcrumbs.Clear();

            if (pathNodes.Count == 0)
            {
                Breadcrumbs.Add(new BrainstormBreadcrumbItem
                {
                    NodeId = null,
                    Title = "Raiz",
                    IsCurrent = true,
                    ShowSeparator = false
                });
                QueueBreadcrumbAutoScroll(false);
                return;
            }

            Breadcrumbs.Add(new BrainstormBreadcrumbItem
            {
                NodeId = null,
                Title = "Raiz",
                IsCurrent = false,
                ShowSeparator = true
            });

            for (int i = 0; i < pathNodes.Count; i++)
            {
                var node = pathNodes[i];
                bool isCurrent = i == pathNodes.Count - 1;

                Breadcrumbs.Add(new BrainstormBreadcrumbItem
                {
                    NodeId = node.Id,
                    Title = node.Title,
                    IsCurrent = isCurrent,
                    ShowSeparator = !isCurrent
                });
            }

            if (string.IsNullOrWhiteSpace(_currentParentTitle))
            {
                _currentParentTitle = pathNodes[^1].Title;
                OnPropertyChanged(nameof(ContextText));
            }

            QueueBreadcrumbAutoScroll(false);
        }

        private async Task LoadNodesAsync()
        {
            var children = await _repository.GetChildrenAsync(_currentParentId);
            Nodes.Clear();
            foreach (var child in children)
                Nodes.Add(child);

            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(EmptyText));
        }

        private async Task AddNodeAsync()
        {
            var editor = new BrainstormNodeEditorPage(isRootLevel: _currentParentId is null);
            await PushModalRootAsync(editor);
            var result = await editor.GetResultAsync();
            if (result is null) return;

            var node = new BrainstormNode
            {
                ParentId = _currentParentId,
                Title = result.Title,
                Description = result.Description
            };

            await _repository.SaveAsync(node);
            await LoadNodesAsync();
            await _toastService.ShowAsync("Elemento creado");
        }

        private async Task GoBackAsync()
        {
            if (_currentParentId is null)
            {
                await GoMenuAsync();
                return;
            }

            await NavigateToParentAsync();
        }

        private async Task GoMenuAsync()
        {
            await Shell.Current.GoToAsync("//MainPage");
        }

        private async Task NavigatePathAsync(BrainstormBreadcrumbItem item)
        {
            if (item is null || item.IsCurrent) return;

            if (item.NodeId is null)
            {
                await NavigateToNodeAsync(null, string.Empty);
                return;
            }

            await NavigateToNodeAsync(item.NodeId.Value, item.Title ?? string.Empty);
        }

        private async Task OpenChildrenAsync(BrainstormNode node)
        {
            if (node is null) return;

            await NavigateToNodeAsync(node.Id, node.Title);
        }

        private async Task ViewNodeAsync(BrainstormNode node)
        {
            if (node is null) return;
            var popup = new BrainstormNodeViewPopupPage(node.Title, node.Description);
            await PushModalRootAsync(popup);
            await popup.ResultTask;
        }

        private async Task EditNodeAsync(BrainstormNode node)
        {
            if (node is null) return;

            var editor = new BrainstormNodeEditorPage(
                isRootLevel: node.ParentId is null,
                isEditMode: true,
                existingTitle: node.Title,
                existingDescription: node.Description);

            await PushModalRootAsync(editor);
            var result = await editor.GetResultAsync();
            if (result is null) return;

            node.Title = result.Title;
            node.Description = result.Description;
            await _repository.UpdateAsync(node);
            await LoadNodesAsync();
            await _toastService.ShowAsync("Elemento actualizado");
        }

        private async Task DeleteNodeAsync(BrainstormNode node)
        {
            if (node is null) return;

            bool confirm = await DisplayAlert(
                "Eliminar",
                $"Se eliminara \"{node.Title}\" y todos sus hijos. Continuar?",
                "Si",
                "No");

            if (!confirm) return;

            await _repository.DeleteBranchAsync(node.Id);
            await LoadNodesAsync();
            await _toastService.ShowAsync("Elemento eliminado");
        }

        private async Task NavigateToNodeAsync(int? parentId, string parentTitle)
        {
            _currentParentId = parentId;
            _currentParentTitle = parentTitle ?? string.Empty;

            RefreshHeaderBindings();
            await EnsureParentContextAsync();
            await LoadBreadcrumbsAsync();
            await LoadNodesAsync();
        }

        private async Task NavigateToParentAsync()
        {
            if (_currentParentId is not int parentId)
            {
                await NavigateToNodeAsync(null, string.Empty);
                return;
            }

            var currentParent = await _repository.GetByIdAsync(parentId);
            if (currentParent?.ParentId is not int nextParentId)
            {
                await NavigateToNodeAsync(null, string.Empty);
                return;
            }

            var nextParent = await _repository.GetByIdAsync(nextParentId);
            await NavigateToNodeAsync(nextParentId, nextParent?.Title ?? string.Empty);
        }

        private async Task PushModalRootAsync(global::Microsoft.Maui.Controls.Page modalPage)
        {
            await EnsureLoadedAsync();
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var navigation = Shell.Current?.Navigation
                                 ?? Window?.Page?.Navigation
                                 ?? Navigation;
                await navigation.PushModalAsync(modalPage);
            });
        }

        private async Task EnsureLoadedAsync()
        {
            if (IsLoaded && Window is not null)
                return;

            var tcs = new TaskCompletionSource();
            void OnLoaded(object? sender, EventArgs args)
            {
                Loaded -= OnLoaded;
                tcs.TrySetResult();
            }

            Loaded += OnLoaded;
            await tcs.Task;
        }

        private void RefreshHeaderBindings()
        {
            OnPropertyChanged(nameof(ContextText));
            OnPropertyChanged(nameof(EmptyText));
            OnPropertyChanged(nameof(AddLabel));
        }

        private void OnBreadcrumbLoaded(object sender, EventArgs e)
        {
            QueueBreadcrumbAutoScroll(false);
        }

        private void OnBreadcrumbSizeChanged(object sender, EventArgs e)
        {
            if (Breadcrumbs.Count == 0)
                return;

            QueueBreadcrumbAutoScroll(false);
        }

        private void QueueBreadcrumbAutoScroll(bool animate)
        {
            _breadcrumbScrollCts?.Cancel();
            _breadcrumbScrollCts?.Dispose();

            _breadcrumbScrollCts = new CancellationTokenSource();
            _ = ScrollBreadcrumbToCurrentAsync(animate, _breadcrumbScrollCts.Token);
        }

        private async Task ScrollBreadcrumbToCurrentAsync(bool animate, CancellationToken cancellationToken)
        {
            if (Breadcrumbs.Count == 0 || BreadcrumbScrollView is null)
                return;

            try
            {
                await Task.Delay(25, cancellationToken);

                double targetX = await GetBreadcrumbScrollTargetXAsync();
                await MainThread.InvokeOnMainThreadAsync(() =>
                    BreadcrumbScrollView.ScrollToAsync(targetX, 0, false));

                await Task.Delay(75, cancellationToken);

                targetX = await GetBreadcrumbScrollTargetXAsync();
                await MainThread.InvokeOnMainThreadAsync(() =>
                    BreadcrumbScrollView.ScrollToAsync(targetX, 0, animate));
            }
            catch (TaskCanceledException)
            {
                // Ignore previous delayed scroll requests.
            }
        }

        private Task<double> GetBreadcrumbScrollTargetXAsync()
        {
            return MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (BreadcrumbScrollView is null || BreadcrumbScrollView.Content is not VisualElement content)
                    return 0d;

                var targetX = content.Width - BreadcrumbScrollView.Width;
                return targetX > 0 ? targetX : 0d;
            });
        }

        private async void OnNavHoverEntered(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement element) return;

            element.CancelAnimations();
            await Task.WhenAll(
                element.ScaleTo(1.04, 120, Easing.CubicOut),
                element.FadeTo(0.94, 120, Easing.CubicOut));
        }

        private async void OnNavHoverExited(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement element) return;

            element.CancelAnimations();
            await Task.WhenAll(
                element.ScaleTo(1, 120, Easing.CubicOut),
                element.FadeTo(1, 120, Easing.CubicOut));
        }

        private async void OnBreadcrumbHoverEntered(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement element) return;

            element.CancelAnimations();
            await Task.WhenAll(
                element.ScaleTo(1.03, 110, Easing.CubicOut),
                element.TranslateTo(0, -1, 110, Easing.CubicOut));
        }

        private async void OnBreadcrumbHoverExited(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement element) return;

            element.CancelAnimations();
            await Task.WhenAll(
                element.ScaleTo(1, 110, Easing.CubicOut),
                element.TranslateTo(0, 0, 110, Easing.CubicOut));
        }

        private async void OnFabPointerEntered(object sender, PointerEventArgs e)
        {
            _isFabPointerInside = true;
            if (sender is not VisualElement element) return;
            await AnimateFabStateAsync(element, 1.08, 1, -2, 120);
        }

        private async void OnFabPointerExited(object sender, PointerEventArgs e)
        {
            _isFabPointerInside = false;
            if (sender is not VisualElement element) return;
            await AnimateFabStateAsync(element, 1, 1, 0, 120);
        }

        private async void OnFabPointerPressed(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement element) return;
            await AnimateFabStateAsync(element, 0.9, 0.9, 0, 90);
        }

        private async void OnFabPointerReleased(object sender, PointerEventArgs e)
        {
            if (sender is not VisualElement element) return;
            var targetScale = _isFabPointerInside ? 1.08 : 1;
            var targetY = _isFabPointerInside ? -2 : 0;
            await AnimateFabStateAsync(element, targetScale, 1, targetY, 120);
        }

        private async void OnFabTapped(object sender, TappedEventArgs e)
        {
            if (sender is not VisualElement element) return;

            element.CancelAnimations();
            await element.ScaleTo(0.92, 65, Easing.CubicOut);
            var targetScale = _isFabPointerInside ? 1.08 : 1;
            await element.ScaleTo(targetScale, 90, Easing.CubicOut);
        }

        private static Task AnimateFabStateAsync(VisualElement element, double scale, double opacity, double translateY, uint duration)
        {
            element.CancelAnimations();
            return Task.WhenAll(
                element.ScaleTo(scale, duration, Easing.CubicOut),
                element.FadeTo(opacity, duration, Easing.CubicOut),
                element.TranslateTo(0, translateY, duration, Easing.CubicOut));
        }
    }

    public sealed class BrainstormBreadcrumbItem
    {
        public int? NodeId { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
        public bool ShowSeparator { get; set; }
    }
}
