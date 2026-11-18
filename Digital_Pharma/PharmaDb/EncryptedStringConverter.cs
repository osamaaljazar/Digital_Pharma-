using Digital_Pharma.Services;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DB.DBB;

public sealed class EncryptedStringConverter(FieldEncryptionService encryption)
    : ValueConverter<string, string>(
        value => encryption.Encrypt(value),
        value => encryption.Decrypt(value));

public sealed class NullableEncryptedStringConverter(FieldEncryptionService encryption)
    : ValueConverter<string?, string?>(
        value => value == null ? null : encryption.Encrypt(value),
        value => value == null ? null : encryption.Decrypt(value));
