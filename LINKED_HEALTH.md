# Linked health review

Patient routes: `/Health/Index`, `/Health/Review`, `/Medications/Search`. Administrator and doctor roles do not gain access to patient records through these routes. Use a separate patient account to exercise the workflow.

Save diagnosed conditions, add/select an exact medication product, then optionally connect a reported allergy to one of its named substances. Saving redirects to a newly computed review. Editing/removing conditions or allergies recomputes findings. Existing text-only medications must be linked explicitly.

## Evidence and limits

- Source API: https://open.fda.gov/apis/drug/label/understanding-the-api-results/
- Two bounded metformin rules: https://dailymed.nlm.nih.gov/dailymed/drugInfo.cfm?setid=73197cb5-3e23-4ac0-b189-f9bc848bce66 (label date 2026-09-03).
- Rule version: 2026-11-10.1. Generic kidney disease never implies severe renal impairment.
- Exact normalized substance strings support allergy and duplicate matching. Salt equivalence, cross-reactivity, dosage calculations, and a comprehensive interaction database are not implemented.
- Label text mentions are context prompts, not verified contraindications or interaction severity. Missing data, malformed snapshots and labels older than 30 days are incomplete. No disease-disease conflict inference and no all-clear result.
- FDA coverage is US-specific. Selecting a similar product does not establish identity. Review label, manufacturer, route and available strength before confirming.
- Clinical validation and local formulary coverage remain required before treating this as a clinical decision system. Older AI interaction routes are separate and have not been validated by this work.

## Storage and maintenance

`PatientConditions` stores catalog IDs; `PatientAllergies` stores patient-reported reactions and optional exact substance keys. `UserMedications` stores the selected label ID, JSON snapshot and retrieval time. Refresh a label by reopening its selection link. Source lookup sends medication search text/label IDs only, not the health profile.

`LinkedHealthProfile` migrates existing MedicalConditions into HealthNotes without truncation. New free notes are limited to 2000 characters in the form; longer legacy notes remain in storage and require explicit editing before resaving. Rowversion guards health profile and allergy changes. Health queries are scoped to the authenticated patient and writes require CSRF tokens.

## Verification

From repository root:

```powershell
dotnet build tests/AccountChecks
dotnet run --project tests/AccountChecks --no-build
dotnet run --project tests/AccountChecks --no-build -- --live-label
dotnet run --project tests/AccountChecks --no-build -- --live-integration
```

2026-11-10: 90 isolated database/HTTP/password assertions passed (including live HTTP medication persistence), plus two live source assertions. Synthetic medications exist only in the disposable test database. UI visual/mobile review and comprehensive clinical validation were not performed. Live selected-label submission, persistence of fetched substances, and redirect to review passed end-to-end in the disposable database.

For the fixed local development database, `--prepare-development` backs up before migrations. Backups are ignored by Git. Backup restoration was not tested.
