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

        public string VariableCountText =>
            Variables.Count == 0 ? "Sin variables" : $"{Variables.Count} variable{(Variables.Count == 1 ? "" : "s")}";
        public string GroupCountText =>
            Groups.Count == 0 ? "Sin grupos" : $"{Groups.Count} grupo{(Groups.Count == 1 ? "" : "s")}";
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
            var groups = await _repository.GetGroupsAsync();
            var selectedId = SelectedGroup?.Id ?? 0;

            Groups.Clear();
            foreach (var group in groups)
            {
                group.Description = NormalizeDescription(group.Description);
                Groups.Add(group);
            }
            OnPropertyChanged(nameof(GroupCountText));

            if (Groups.Count == 0)
            {
                var defaultGroup = await _repository.SaveGroupAsync(new VariableGroup
                {
                    Name = "General",
                    Description = "Variables generales de trabajo"
                });
                Groups.Add(defaultGroup);
                OnPropertyChanged(nameof(GroupCountText));
            }

            SelectedGroup = Groups.FirstOrDefault(g => g.Id == selectedId) ?? Groups.FirstOrDefault();
        }

        private async Task LoadVariablesAsync()
        {
            Variables.Clear();

            if (SelectedGroup is null)
            {
                OnPropertyChanged(nameof(VariableCountText));
                return;
            }

            var items = await _repository.GetByGroupAsync(SelectedGroup.Id);
            foreach (var item in items)
            {
                item.Description = NormalizeDescription(item.Description);
                if (item.IsSecret)
                    item.Value = SecretMask;

                Variables.Add(item);
            }

            OnPropertyChanged(nameof(VariableCountText));
            OnPropertyChanged(nameof(CurrentGroupTitle));
        }

        private async Task OpenGroupAsync(VariableGroup group)
        {
            if (group is null) return;
            SelectedGroup = group;
            IsGroupView = false;
            await LoadVariablesAsync();
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

            Groups.Add(group);
            SelectedGroup = group;
            OnPropertyChanged(nameof(GroupCountText));
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
            SelectedGroup = Groups.FirstOrDefault(g => g.Id == group.Id) ?? SelectedGroup;
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
            if (Groups.Count == 1)
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

            var previous = Groups.FirstOrDefault(g => g.Id != deleteId);
            await LoadGroupsAsync();
            SelectedGroup = previous ?? Groups.FirstOrDefault();
            await LoadVariablesAsync();
            IsGroupView = true;
        }

        private async Task AddVariableAsync()
        {
            if (SelectedGroup is null) return;

            var editorResult = await ShowVariableEditorAsync();
            if (editorResult is null) return;

            string value = editorResult.IsSecret ? SecretMask : editorResult.Value;
            string encrypted = editorResult.IsSecret ? editorResult.EncryptedValue : string.Empty;

            var variable = await _repository.SaveVariableAsync(new WorkVariable
            {
                GroupId = SelectedGroup.Id,
                VariableKey = editorResult.Key,
                Description = editorResult.Description,
                IsSecret = editorResult.IsSecret,
                Value = editorResult.IsSecret ? SecretMask : value,
                EncryptedValue = encrypted
            });
            variable.Description = NormalizeDescription(variable.Description);

            if (variable.IsSecret)
                variable.Value = SecretMask;

            Variables.Add(variable);
            SortVariables();
            OnPropertyChanged(nameof(VariableCountText));
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

            variable.VariableKey = editorResult.Key;
            variable.Description = NormalizeDescription(editorResult.Description);
            variable.IsSecret = editorResult.IsSecret;
            variable.Value = value;
            variable.EncryptedValue = encrypted;

            await _repository.SaveVariableAsync(variable);
            if (variable.IsSecret)
                variable.Value = SecretMask;
            SortVariables();
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
            Variables.Remove(variable);
            OnPropertyChanged(nameof(VariableCountText));
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
            if (!await _secretVaultService.EnsurePinAsync(this)) return false;
            _secretVaultService.Lock();
            return await _secretVaultService.UnlockAsync(this);
        }

        private void SortVariables()
        {
            var sorted = Variables.OrderBy(v => v.VariableKey, StringComparer.OrdinalIgnoreCase).ToList();
            Variables.Clear();
            foreach (var item in sorted)
                Variables.Add(item);
        }

        private static string NormalizeDescription(string description)
        {
            return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        }
    }
}



