using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Elysium.WorkStation.Views;

namespace Elysium.WorkStation.Services
{
    public class SecretVaultService : ISecretVaultService
    {
        private const string PinSaltKey = "vault_pin_salt";
        private const string PinVerifierKey = "vault_pin_verifier";
        private const string VerifierText = "ElysiumSecretVault:v1";
        private static readonly Regex PinRegex = new(@"^\d{4,}$", RegexOptions.Compiled);
        private byte[] _sessionKey;

        public bool IsPinConfigured =>
            !string.IsNullOrWhiteSpace(Preferences.Default.Get(PinSaltKey, string.Empty)) &&
            !string.IsNullOrWhiteSpace(Preferences.Default.Get(PinVerifierKey, string.Empty));

        public bool IsUnlocked => _sessionKey is not null;

        public bool IsValidPin(string pin)
        {
            return !string.IsNullOrWhiteSpace(pin) && PinRegex.IsMatch(pin.Trim());
        }

        public bool TryUnlockWithPin(string pin)
        {
            if (!IsPinConfigured) return false;
            if (!IsValidPin(pin)) return false;

            var saltText = Preferences.Default.Get(PinSaltKey, string.Empty);
            var verifierText = Preferences.Default.Get(PinVerifierKey, string.Empty);
            if (string.IsNullOrWhiteSpace(saltText) || string.IsNullOrWhiteSpace(verifierText))
                return false;

            try
            {
                var salt = Convert.FromBase64String(saltText);
                var key = DeriveKey(pin.Trim(), salt);
                var plain = DecryptWithKey(verifierText, key);
                if (!string.Equals(plain, VerifierText, StringComparison.Ordinal))
                    return false;

                _sessionKey = key;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SetPin(string pin)
        {
            if (!IsValidPin(pin)) return false;

            var normalizedPin = pin.Trim();
            var salt = RandomNumberGenerator.GetBytes(16);
            var key = DeriveKey(normalizedPin, salt);
            var verifier = EncryptWithKey(VerifierText, key);

            Preferences.Default.Set(PinSaltKey, Convert.ToBase64String(salt));
            Preferences.Default.Set(PinVerifierKey, verifier);
            _sessionKey = key;
            return true;
        }

        public async Task<bool> EnsurePinAsync(Page page)
        {
            if (IsPinConfigured) return true;

            var pin = await PromptPinAsync(
                page,
                "PIN para secretos",
                "Define un PIN numerico de al menos 4 digitos para cifrar secretos.",
                "Ej. 1234");

            if (pin is null) return false;
            pin = pin.Trim();
            if (!IsValidPin(pin))
            {
                await page.DisplayAlert("PIN invalido", "Debe tener al menos 4 digitos numericos.", "OK");
                return false;
            }

            var confirm = await PromptPinAsync(
                page,
                "Confirmar PIN",
                "Vuelve a ingresar el PIN.",
                "PIN");

            if (confirm is null) return false;
            confirm = confirm.Trim();

            if (!string.Equals(pin, confirm, StringComparison.Ordinal))
            {
                await page.DisplayAlert("PIN", "Los PIN no coinciden.", "OK");
                return false;
            }

            return SetPin(pin);
        }

        public async Task<bool> UnlockAsync(Page page)
        {
            if (IsUnlocked) return true;
            if (!IsPinConfigured)
            {
                var created = await EnsurePinAsync(page);
                return created && IsUnlocked;
            }

            var pin = await PromptPinAsync(
                page,
                "Desbloquear secretos",
                "Ingresa tu PIN para usar variables secretas.",
                "PIN");

            if (pin is null) return false;
            pin = pin.Trim();
            if (!IsValidPin(pin))
            {
                await page.DisplayAlert("PIN invalido", "Debe tener al menos 4 digitos numericos.", "OK");
                return false;
            }

            if (!TryUnlockWithPin(pin))
            {
                await page.DisplayAlert("PIN incorrecto", "No fue posible desbloquear secretos.", "OK");
                return false;
            }

            return true;
        }

        public void Lock()
        {
            _sessionKey = null;
        }

        public string Encrypt(string plainText)
        {
            if (!IsUnlocked) throw new InvalidOperationException("Vault bloqueado.");
            return EncryptWithKey(plainText, _sessionKey);
        }

        public string Decrypt(string cipherText)
        {
            if (!IsUnlocked) throw new InvalidOperationException("Vault bloqueado.");
            return DecryptWithKey(cipherText, _sessionKey);
        }

        private static async Task<string?> PromptPinAsync(Page page, string title, string message, string placeholder)
        {
            var popup = new PinPromptPage(title, message, placeholder);
            await page.Navigation.PushModalAsync(popup);
            return await popup.ResultTask;
        }

        private static byte[] DeriveKey(string pin, byte[] salt)
        {
            using var derive = new Rfc2898DeriveBytes(
                pin,
                salt,
                100_000,
                HashAlgorithmName.SHA256);
            return derive.GetBytes(32);
        }

        private static string EncryptWithKey(string plainText, byte[] key)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = new byte[plainBytes.Length];
            var tag = new byte[16];

            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

            var payload = new byte[nonce.Length + tag.Length + cipherBytes.Length];
            Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipherBytes, 0, payload, nonce.Length + tag.Length, cipherBytes.Length);
            return Convert.ToBase64String(payload);
        }

        private static string DecryptWithKey(string cipherText, byte[] key)
        {
            var payload = Convert.FromBase64String(cipherText);
            if (payload.Length < 12 + 16)
                throw new CryptographicException("Invalid payload.");

            var nonce = new byte[12];
            var tag = new byte[16];
            var cipher = new byte[payload.Length - nonce.Length - tag.Length];

            Buffer.BlockCopy(payload, 0, nonce, 0, nonce.Length);
            Buffer.BlockCopy(payload, nonce.Length, tag, 0, tag.Length);
            Buffer.BlockCopy(payload, nonce.Length + tag.Length, cipher, 0, cipher.Length);

            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
    }
}
