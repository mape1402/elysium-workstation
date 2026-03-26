using Elysium.WorkStation.Models;
using Elysium.WorkStation.Services;

namespace Elysium.WorkStation.Views
{
    public sealed record VariableEditorResult(
        string Key,
        string Value,
        string Description,
        bool IsSecret,
        string EncryptedValue,
        bool KeepExistingSecret);

    public partial class VariableEditorPage : ContentPage
    {
        private const string HiddenMask = "********";

        private readonly ISecretVaultService _secretVaultService;
        private readonly WorkVariable _existingVariable;
        private readonly bool _isEditMode;
        private readonly TaskCompletionSource<VariableEditorResult?> _resultSource = new();

        private bool _isSecretMode;
        private string _encryptedValue = string.Empty;

        public string PageTitle => _isEditMode ? "Editar variable" : "Nueva variable";
        public string HeaderText => _isEditMode ? "Editar datos de la variable" : "Crear nueva variable";
        public string SaveButtonText => _isEditMode ? "Guardar cambios" : "Crear variable";
        public string SecretToggleTooltip => _isSecretMode ? "Mostrar valor" : "Ocultar y guardar como secreto";
        public string SecretActionText => _isSecretMode ? "Mostrar" : "Cifrar";
        public Task<VariableEditorResult?> ResultTask => _resultSource.Task;

        public VariableEditorPage(
            ISecretVaultService secretVaultService,
            WorkVariable existingVariable = null)
        {
            _secretVaultService = secretVaultService;
            _existingVariable = existingVariable;
            _isEditMode = existingVariable is not null;
            _isSecretMode = existingVariable?.IsSecret == true;

            InitializeComponent();
            BindingContext = this;

            if (_isEditMode)
            {
                KeyEntry.Text = existingVariable.VariableKey;
                DescriptionEditor.Text = existingVariable.Description;

                if (existingVariable.IsSecret)
                {
                    _encryptedValue = existingVariable.EncryptedValue;
                    ValueEntry.Text = HiddenMask;
                }
                else
                {
                    ValueEntry.Text = existingVariable.Value;
                }
            }

            ApplyModeToUi();
        }

        private async void OnSecretToggleClicked(object sender, EventArgs e)
        {
            var nextSecretMode = !_isSecretMode;
            if (!await EnsureSecretAccessAsync()) return;

            if (nextSecretMode)
            {
                var plain = ValueEntry.Text ?? string.Empty;
                if (_isEditMode && _existingVariable.IsSecret && plain == HiddenMask)
                {
                    _encryptedValue = _existingVariable.EncryptedValue;
                }
                else
                {
                    _encryptedValue = _secretVaultService.Encrypt(plain);
                }

                ValueEntry.Text = HiddenMask;
                _isSecretMode = true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_encryptedValue))
                {
                    ValueEntry.Text = string.Empty;
                }
                else
                {
                    ValueEntry.Text = _secretVaultService.Decrypt(_encryptedValue);
                }

                _isSecretMode = false;
            }

            ApplyModeToUi();
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            _resultSource.TrySetResult(null);
            await Navigation.PopModalAsync();
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            var key = KeyEntry.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                await DisplayAlert("Variable", "La clave no puede estar vacia.", "OK");
                return;
            }

            var value = ValueEntry.Text ?? string.Empty;
            var keepExistingSecret = _isEditMode &&
                                     _existingVariable.IsSecret &&
                                     _isSecretMode &&
                                     string.Equals(value, HiddenMask, StringComparison.Ordinal) &&
                                     string.Equals(_encryptedValue, _existingVariable.EncryptedValue, StringComparison.Ordinal);

            if (_isSecretMode && !keepExistingSecret)
            {
                var requiresEncryption = string.IsNullOrWhiteSpace(_encryptedValue) ||
                                         !string.Equals(value, HiddenMask, StringComparison.Ordinal);

                if (requiresEncryption)
                {
                    try
                    {
                        if (!await EnsureSecretAccessAsync()) return;

                        var plain = string.Equals(value, HiddenMask, StringComparison.Ordinal)
                            ? string.Empty
                            : value;

                        _encryptedValue = _secretVaultService.Encrypt(plain);
                    }
                    catch (Exception ex)
                    {
                        await DisplayAlert("Variable", $"No fue posible cifrar el valor: {ex.Message}", "OK");
                        return;
                    }
                }
            }

            _resultSource.TrySetResult(new VariableEditorResult(
                key,
                _isSecretMode ? HiddenMask : value,
                DescriptionEditor.Text?.Trim() ?? string.Empty,
                _isSecretMode,
                _encryptedValue,
                keepExistingSecret));

            await Navigation.PopModalAsync();
        }

        private void ApplyModeToUi()
        {
            ValueEntry.IsPassword = _isSecretMode;
            ValueLabel.Text = _isSecretMode ? "Valor (secreto)" : "Valor";
            SecretHintLabel.IsVisible = _isEditMode;
            SecretToggleButton.Text = SecretActionText;
            SecretToggleButton.BackgroundColor = _isSecretMode
                ? Color.FromArgb("#2F5AA8")
                : Color.FromArgb("#5B6475");
            SecretToggleButton.TextColor = Colors.White;
            OnPropertyChanged(nameof(SecretToggleTooltip));
            OnPropertyChanged(nameof(SecretActionText));
        }

        private async Task<bool> EnsureSecretAccessAsync()
        {
            _secretVaultService.Lock();

            if (!_secretVaultService.IsPinConfigured)
                return await _secretVaultService.EnsurePinAsync(this);

            return await _secretVaultService.UnlockAsync(this);
        }
    }
}
