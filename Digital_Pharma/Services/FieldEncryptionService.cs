using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Digital_Pharma.Services;

/// <summary>
/// Protects sensitive database values with authenticated encryption.
/// The prefix permits safe, gradual migration of legacy plaintext values.
/// </summary>
public sealed class FieldEncryptionService(IDataProtectionProvider provider)
{
    public const string Prefix = "dp:v1:";
    private readonly IDataProtector _protector = provider.CreateProtector("DigitalPharma.DatabaseFields.v1");

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        return Prefix + _protector.Protect(plaintext);
    }

    public string Decrypt(string storedValue)
    {
        if (string.IsNullOrEmpty(storedValue) || !storedValue.StartsWith(Prefix, StringComparison.Ordinal))
            return storedValue; // Legacy plaintext; migration will protect it on the next controlled write.

        try
        {
            return _protector.Unprotect(storedValue[Prefix.Length..]);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "A protected database value could not be decrypted. Verify the Data Protection key ring; do not overwrite the value.",
                exception);
        }
    }
}
