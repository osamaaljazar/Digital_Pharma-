using System.Data;
using DB.DBB;
using Microsoft.EntityFrameworkCore;

namespace Digital_Pharma.Services;

/// <summary>One-time, idempotent migration of legacy plaintext database fields.</summary>
public sealed class SensitiveDataMigrator(PharmaDbContext db, FieldEncryptionService encryption)
{
    private static readonly (string Table, string Column)[] Fields =
    [
        ("Users", "PhoneNumber"), ("Users", "MedicalConditions"), ("Users", "HealthNotes"),
        ("Searches", "Drug1Name"), ("Searches", "Drug2Name"), ("Searches", "ResultMessage"),
        ("Searches", "AiAnalysis"), ("Searches", "Recommendation"), ("Searches", "PatientContext"),
        ("Searches", "SafeAlternatives"),
        ("PatientAllergies", "Name"), ("PatientAllergies", "SubstanceKey"), ("PatientAllergies", "Reaction"),
        ("UserMedications", "DrugName"), ("UserMedications", "Notes"), ("UserMedications", "LabelJson"),
        ("UserMedications", "DailyTimes"), ("UserMedications", "Instructions"),
        ("MedicationChanges", "Reason"), ("MedicationChanges", "BeforeJson"), ("MedicationChanges", "AfterJson"),
        ("DoseRecords", "DrugName"), ("DoseChanges", "Reason"),
        ("Doctors", "LicenseNumber"), ("Doctors", "LicensingAuthority"), ("Doctors", "ContactPhone"),
        ("DoctorReviews", "Note"), ("DoctorReviews", "ApplicationSnapshot"),
        ("TreatmentChanges", "DoctorName"), ("TreatmentChanges", "Reason"),
        ("TreatmentChanges", "BeforeJson"), ("TreatmentChanges", "AfterJson")
    ];

    public async Task<int> EncryptLegacyValuesAsync(CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changed = 0;

        try
        {
            foreach (var (table, column) in Fields)
            {
                var values = new List<(int Id, string Value)>();
                await using (var select = connection.CreateCommand())
                {
                    select.Transaction = transaction;
                    select.CommandText = $"""
                        IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
                           AND COL_LENGTH(N'[dbo].[{table}]', N'{column}') IS NOT NULL
                        SELECT [Id], [{column}] FROM [dbo].[{table}]
                        WHERE [{column}] IS NOT NULL AND [{column}] <> N'' AND [{column}] NOT LIKE N'dp:v1:%';
                        """;
                    await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                        values.Add((reader.GetInt32(0), reader.GetString(1)));
                }

                foreach (var (id, value) in values)
                {
                    var protectedValue = encryption.Encrypt(value);
                    if (encryption.Decrypt(protectedValue) != value)
                        throw new InvalidOperationException("Encryption round-trip verification failed.");

                    await using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = $"UPDATE [dbo].[{table}] SET [{column}] = @value WHERE [Id] = @id AND [{column}] = @original;";
                    var valueParameter = update.CreateParameter(); valueParameter.ParameterName = "@value"; valueParameter.Value = protectedValue;
                    var idParameter = update.CreateParameter(); idParameter.ParameterName = "@id"; idParameter.Value = id;
                    var originalParameter = update.CreateParameter(); originalParameter.ParameterName = "@original"; originalParameter.Value = value;
                    update.Parameters.Add(valueParameter); update.Parameters.Add(idParameter); update.Parameters.Add(originalParameter);
                    changed += await update.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return changed;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
