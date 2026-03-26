namespace Elysium.WorkStation.Services
{
    public interface ISecretVaultService
    {
        bool IsPinConfigured { get; }
        bool IsUnlocked { get; }

        Task<bool> EnsurePinAsync(Page page);
        Task<bool> UnlockAsync(Page page);
        void Lock();

        string Encrypt(string plainText);
        string Decrypt(string cipherText);
    }
}
