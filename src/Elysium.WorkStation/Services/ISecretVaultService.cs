namespace Elysium.WorkStation.Services
{
    public interface ISecretVaultService
    {
        bool IsPinConfigured { get; }
        bool IsUnlocked { get; }
        bool IsValidPin(string pin);
        bool TryUnlockWithPin(string pin);
        bool SetPin(string pin);

        Task<bool> EnsurePinAsync(Page page);
        Task<bool> UnlockAsync(Page page);
        void Lock();

        string Encrypt(string plainText);
        string Decrypt(string cipherText);
    }
}
