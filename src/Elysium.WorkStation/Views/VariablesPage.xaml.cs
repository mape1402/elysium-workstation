using System.Collections.ObjectModel;
using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;

namespace Elysium.WorkStation.Views
{
    public partial class VariablesPage : ContentPage
    {
        private const string SecretMask = "••••••••";
        private readonly IVariableRepository _repository;
        private readonly ISecretVaultService _secretVaultService;
        private readonly IToastService _toastService;
        private List<VariableGroup> _allGroups = [];
        private List<WorkVariable> _allVariables = [];
        private string _groupSearchText = string.Empty;
        private string _variableSearchText = string.Empty;
        private bool _isFabPointerInside;

        public ObservableCollection<VariableGroup> Groups { get; } = [];
        public ObservableCollection<WorkVariable> Variables { get; } = [];

        private bool _isGroupView = true;
        public bool IsGroupView
        {
            get => _isGroupView;
            set
            {
                _isGroupView = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsVariablesView));
                OnPropertyChanged(nameof(PrimaryAddTooltip));
            }
        }
        public bool IsVariablesView => !IsGroupView;

        public string GroupSearchText
        {
            get => _groupSearchText;
            set
            {
                var next = value ?? string.Empty;
                if (string.Equals(_groupSearchText, next, StringComparison.Ordinal))
                    return;

                _groupSearchText = next;
                OnPropertyChanged();
                ApplyGroupFilter();
            }
        }

        public string VariableSearchText
        {
            get => _variableSearchText;
            set
            {
                var next = value ?? string.Empty;
                if (string.Equals(_variableSearchText, next, StringComparison.Ordinal))
                    return;

                _variableSearchText = next;
                OnPropertyChanged();
                ApplyVariableFilter();
            }
        }

        private VariableGroup _selectedGroup;
        public VariableGroup SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                _selectedGroup = value;
                OnPropertyChanged();
                _ = LoadVariablesAsync();
            }
        }

        public string VariableCountText
        {
            get
            {
                var total = _allVariables.Count;
                if (total == 0)
                    return "Sin variables";

                var filtered = Variables.Count;
                if (string.IsNullOrWhiteSpace(VariableSearchText))
                    return $"{total} variable{(total == 1 ? string.Empty : "s")}";

                return $"{filtered} de {total} variable{(total == 1 ? string.Empty : "s")}";
            }
        }

        public string GroupCountText
        {
            get
            {
                var total = _allGroups.Count;
                if (total == 0)
                    return "Sin grupos";

                var filtered = Groups.Count;
                if (string.IsNullOrWhiteSpace(GroupSearchText))
                    return $"{total} grupo{(total == 1 ? string.Empty : "s")}";

                return $"{filtered} de {total} grupo{(total == 1 ? string.Empty : "s")}";
            }
        }
        public string CurrentGroupTitle =>
            SelectedGroup is null ? string.Empty : $"Grupo: {SelectedGroup.Name}";
        public string PrimaryAddTooltip => IsGroupView ? "Agregar grupo" : "Agregar variable";

        public Command ReloadCommand { get; }
        public Command AddGroupCommand { get; }
        public Command<VariableGroup> OpenGroupCommand { get; }
        public Command<VariableGroup> EditGroupFromCardCommand { get; }
        public Command<VariableGroup> DeleteGroupFromCardCommand { get; }
        public Command BackToGroupsCommand { get; }
        public Command EditGroupCommand { get; }
        public Command DeleteGroupCommand { get; }
        public Command AddVariableCommand { get; }
        public Command<WorkVariable> EditVariableCommand { get; }
        public Command<WorkVariable> DeleteVariableCommand { get; }
        public Command<WorkVariable> CopyVariableCommand { get; }
        public Command<WorkVariable> RevealVariableCommand { get; }
        public Command<WorkVariable> VariableCardCommand { get; }
        public Command LockSecretsCommand { get; }
        public Command PrimaryAddCommand { get; }

        public VariablesPage(
            IVariableRepository repository,
            ISecretVaultService secretVaultService,
            IToastService toastService)
        {
            _repository = repository;
            _secretVaultService = secretVaultService;
            _toastService = toastService;

            ReloadCommand = new Command(async () =>
            {
                await LoadGroupsAsync();
                await LoadVariablesAsync();
            });

            AddGroupCommand = new Command(async () => await AddGroupAsync());
            OpenGroupCommand = new Command<VariableGroup>(async group => await OpenGroupAsync(group));
            EditGroupFromCardCommand = new Command<VariableGroup>(async group => await EditGroupAsync(group));
            DeleteGroupFromCardCommand = new Command<VariableGroup>(async group => await DeleteGroupAsync(group));
            BackToGroupsCommand = new Command(() => IsGroupView = true);
            EditGroupCommand = new Command(async () => await EditGroupAsync());
            DeleteGroupCommand = new Command(async () => await DeleteGroupAsync());
            AddVariableCommand = new Command(async () => await AddVariableAsync());
            EditVariableCommand = new Command<WorkVariable>(async variable => await EditVariableAsync(variable));
            DeleteVariableCommand = new Command<WorkVariable>(async variable => await DeleteVariableAsync(variable));
            CopyVariableCommand = new Command<WorkVariable>(async variable => await CopyVariableAsync(variable));
            RevealVariableCommand = new Command<WorkVariable>(async variable => await RevealVariableAsync(variable));
            VariableCardCommand = new Command<WorkVariable>(async variable => await ShowVariableValueAsync(variable));
            LockSecretsCommand = new Command(() => _secretVaultService.Lock());
            PrimaryAddCommand = new Command(async () =>
            {
                if (IsGroupView)
                    await AddGroupAsync();
                else
                    await AddVariableAsync();
            });

            InitializeComponent();
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadGroupsAsync();
            await LoadVariablesAsync();
        }

        private async Task LoadGroupsAsync()
        {
            var groups = (await _repository.GetGroupsAsync()).ToList();
            var selectedId = SelectedGroup?.Id ?? 0;

            foreach (var group in groups)
                group.Description = NormalizeDescription(group.Description);

            if (groups.Count == 0)
            {
                var defaultGroup = await _repository.SaveGroupAsync(new VariableGroup
                {
                    Name = "General",
                    Description = "Variables generales de trabajo"
                });
                defaultGroup.Description = NormalizeDescription(defaultGroup.Description);
                groups.Add(defaultGroup);
            }

            _allGroups = groups
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ApplyGroupFilter();

            SelectedGroup = _allGroups.FirstOrDefault(g => g.Id == selectedId) ?? _allGroups.FirstOrDefault();
        }

        private async Task LoadVariablesAsync()
        {
            if (SelectedGroup is null)
            {
                _allVariables.Clear();
                ApplyVariableFilter();
                OnPropertyChanged(nameof(CurrentGroupTitle));
                return;
            }

            var items = await _repository.GetByGroupAsync(SelectedGroup.Id);
            foreach (var item in items)
            {
                item.Description = NormalizeDescription(item.Description);
                if (item.IsSecret)
                    item.Value = SecretMask;
            }

            _allVariables = items
                .OrderBy(v => v.VariableKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ApplyVariableFilter();
            OnPropertyChanged(nameof(CurrentGroupTitle));
        }

        private Task OpenGroupAsync(VariableGroup group)
        {
            if (group is null) return Task.CompletedTask;
            SelectedGroup = group;
            IsGroupView = false;
            return Task.CompletedTask;
        }

        private async Task AddGroupAsync()
        {
            var editorResult = await ShowGroupEditorAsync();
            if (editorResult is null) return;

            var group = await _repository.SaveGroupAsync(new VariableGroup
            {
                Name = editorResult.Name,
                Description = editorResult.Description
            });
            group.Description = NormalizeDescription(group.Description);

            _allGroups.Add(group);
            _allGroups = _allGroups
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ApplyGroupFilter();

            SelectedGroup = _allGroups.FirstOrDefault(g => g.Id == group.Id) ?? group;
            OnPropertyChanged(nameof(CurrentGroupTitle));
        }

        private async Task EditGroupAsync(VariableGroup group = null)
        {
            group ??= SelectedGroup;
            if (group is null) return;

            var editorResult = await ShowGroupEditorAsync(group);
            if (editorResult is null) return;

            group.Name = editorResult.Name;
            group.Description = editorResult.Description;
            await _repository.SaveGroupAsync(group);
            await LoadGroupsAsync();
            SelectedGroup = _allGroups.FirstOrDefault(g => g.Id == group.Id) ?? SelectedGroup;
            OnPropertyChanged(nameof(CurrentGroupTitle));
        }

        private async Task<GroupEditorResult?> ShowGroupEditorAsync(VariableGroup existingGroup = null)
        {
            var editorPage = new GroupEditorPage(existingGroup);
            await Navigation.PushModalAsync(editorPage);
            return await editorPage.ResultTask;
        }

        private async Task DeleteGroupAsync(VariableGroup group = null)
        {
            group ??= SelectedGroup;
            if (group is null) return;
            if (_allGroups.Count == 1)
            {
                await DisplayAlert("Grupo", "Debe existir al menos un grupo.", "OK");
                return;
            }

            bool confirm = await DisplayAlert(
                "Eliminar grupo",
                $"¿Eliminar el grupo \"{group.Name}\" y todas sus variables?",
                "Sí",
                "No");
            if (!confirm) return;

            int deleteId = group.Id;
            await _repository.DeleteGroupAsync(deleteId);

            var previous = _allGroups.FirstOrDefault(g => g.Id != deleteId);
            await LoadGroupsAsync();
            SelectedGroup = previous is null
                ? _allGroups.FirstOrDefault()
                : _allGroups.FirstOrDefault(g => g.Id == previous.Id) ?? _allGroups.FirstOrDefault();
            await LoadVariablesAsync();
            IsGroupView = true;
        }

        private async Task AddVariableAsync()
        {
            if (SelectedGroup is null) return;

            var editorResult = await ShowVariableEditorAsync();
            if (editorResult is null) return;

            try
            {
                string value = editorResult.IsSecret ? SecretMask : editorResult.Value;
                string encrypted = editorResult.IsSecret ? editorResult.EncryptedValue : string.Empty;

                var variable = await _repository.SaveVariableAsync(new WorkVariable
                {
                    GroupId = SelectedGroup.Id,
                    VariableKey = editorResult.Key,
                    Description = NormalizeDescriptionForStorage(editorResult.Description),
                    IsSecret = editorResult.IsSecret,
                    Value = editorResult.IsSecret ? SecretMask : value,
                    EncryptedValue = encrypted
                });
                variable.Description = NormalizeDescription(variable.Description);

                if (variable.IsSecret)
                    variable.Value = SecretMask;

                _allVariables.Add(variable);
                SortVariables();
                ApplyVariableFilter();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Variable", ex.Message, "OK");
            }
        }
        private async Task EditVariableAsync(WorkVariable variable)
        {
            if (variable is null) return;

            var editorResult = await ShowVariableEditorAsync(variable);
            if (editorResult is null) return;

            string value;
            string encrypted;

            if (editorResult.IsSecret)
            {
                value = SecretMask;
                encrypted = editorResult.KeepExistingSecret
                    ? variable.EncryptedValue
                    : editorResult.EncryptedValue;
            }
            else
            {
                value = editorResult.Value;
                encrypted = string.Empty;
            }

            try
            {
                variable.VariableKey = editorResult.Key;
                variable.Description = NormalizeDescriptionForStorage(editorResult.Description);
                variable.IsSecret = editorResult.IsSecret;
                variable.Value = value;
                variable.EncryptedValue = encrypted;

                await _repository.SaveVariableAsync(variable);
                variable.Description = NormalizeDescription(variable.Description);
                if (variable.IsSecret)
                    variable.Value = SecretMask;
                SortVariables();
                ApplyVariableFilter();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Variable", ex.Message, "OK");
            }
        }

        private async Task<VariableEditorResult?> ShowVariableEditorAsync(WorkVariable existingVariable = null)
        {
            var editorPage = new VariableEditorPage(_secretVaultService, existingVariable);
            await Navigation.PushModalAsync(editorPage);
            return await editorPage.ResultTask;
        }

        private async Task DeleteVariableAsync(WorkVariable variable)
        {
            if (variable is null) return;

            bool confirm = await DisplayAlert(
                "Eliminar variable",
                $"¿Eliminar \"{variable.VariableKey}\"?",
                "Sí",
                "No");
            if (!confirm) return;

            await _repository.DeleteVariableAsync(variable.Id);
            _allVariables.RemoveAll(v => v.Id == variable.Id);
            ApplyVariableFilter();
        }

        private async Task ShowVariableValueAsync(WorkVariable variable)
        {
            if (variable is null) return;

            string value;
            if (variable.IsSecret)
            {
                value = await TryResolveSecretAsync(variable);
                if (value is null) return;
            }
            else
            {
                value = variable.Value;
            }

            var popup = new VariableValuePopupPage(variable.VariableKey, variable.Description, value);
            await Navigation.PushModalAsync(popup);
            await popup.ResultTask;
        }
        private async Task CopyVariableAsync(WorkVariable variable)
        {
            if (variable is null) return;

            var isSecret = variable.IsSecret;
            string resolved;
            if (isSecret)
            {
                resolved = await TryResolveSecretAsync(variable);
                if (resolved is null) return;
            }
            else
            {
                resolved = variable.Value;
            }

            await Clipboard.Default.SetTextAsync(resolved);
            await _toastService.ShowAsync("Valor copiado ⧉");
        }
        private async Task RevealVariableAsync(WorkVariable variable)
        {
            if (variable is null) return;

            if (!variable.IsSecret)
            {
                await DisplayAlert("Variable", variable.Value, "OK");
                return;
            }

            var value = await TryResolveSecretAsync(variable);
            if (value is null) return;

            variable.Value = value;
            var index = Variables.IndexOf(variable);
            if (index >= 0)
            {
                Variables.RemoveAt(index);
                Variables.Insert(index, variable);
            }
        }

        private async Task<string> TryResolveSecretAsync(WorkVariable variable)
        {
            if (!variable.IsSecret) return variable.Value;
            if (string.IsNullOrWhiteSpace(variable.EncryptedValue)) return string.Empty;

            if (!await EnsureSecretAccessAsync()) return null;

            try
            {
                return _secretVaultService.Decrypt(variable.EncryptedValue);
            }
            catch
            {
                await DisplayAlert("Error", "No fue posible descifrar el secreto.", "OK");
                return null;
            }
        }
        private async Task<bool> EnsureSecretAccessAsync()
        {
            _secretVaultService.Lock();

            if (!_secretVaultService.IsPinConfigured)
                return await _secretVaultService.EnsurePinAsync(this);

            return await _secretVaultService.UnlockAsync(this);
        }

        private void SortVariables()
        {
            _allVariables = _allVariables
                .OrderBy(v => v.VariableKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void ApplyGroupFilter()
        {
            var filtered = _allGroups.AsEnumerable();
            var query = GroupSearchText?.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(g =>
                    ContainsIgnoreCase(g.Name, query) ||
                    ContainsIgnoreCase(g.Description, query));
            }

            Groups.Clear();
            foreach (var group in filtered)
                Groups.Add(group);

            OnPropertyChanged(nameof(GroupCountText));
        }

        private void ApplyVariableFilter()
        {
            var filtered = _allVariables.AsEnumerable();
            var query = VariableSearchText?.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(v =>
                    ContainsIgnoreCase(v.VariableKey, query) ||
                    ContainsIgnoreCase(v.Description, query));
            }

            Variables.Clear();
            foreach (var variable in filtered)
                Variables.Add(variable);

            OnPropertyChanged(nameof(VariableCountText));
        }

        private static bool ContainsIgnoreCase(string source, string term)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(term))
                return false;

            return source.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeDescription(string description)
        {
            return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        }

        private static string NormalizeDescriptionForStorage(string description)
        {
            return string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim();
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
}



