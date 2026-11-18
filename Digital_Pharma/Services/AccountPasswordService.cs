using System.Security.Cryptography;
using System.Text;
using Digital_Pharma.Models;
using Microsoft.AspNetCore.Identity;

namespace Digital_Pharma.Services;

public sealed class AccountPasswordService
{
    private readonly PasswordHasher<User> _hasher = new();
    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public PasswordVerificationResult Verify(User user, string password)
    {
        if (string.IsNullOrEmpty(user.PasswordHash)) return PasswordVerificationResult.Failed;
        try
        {
            var bytes = Convert.FromBase64String(user.PasswordHash);
            // Previous releases stored a raw SHA-256 digest. Upgrade only after proof of the password.
            if (bytes.Length == 32)
                return CryptographicOperations.FixedTimeEquals(bytes, SHA256.HashData(Encoding.UTF8.GetBytes(password)))
                    ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Failed;
            return _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        }
        catch (FormatException) { return PasswordVerificationResult.Failed; }
    }
}
