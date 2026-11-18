# Doctor approval workflow

Implemented 2026-11-10. This is account credential review, not a patient-care portal. Patient linking and medication management remain separate work.

## Access and routing

- One registration/login page. Only `patient` or `doctor` is accepted as the registration intent; no public admin option.
- Server-managed `IsAdmin` grants administration. `IsDoctorApplicant` records application intent. A doctor receives the Doctor role only when their stored application is Approved AND IsVerified is true.
- Roles are rebuilt from the database on every authenticated request. Approval, suspension and administrator revocation affect already-issued cookies immediately on the next request.
- Applicants land on their own application/status page. Approved doctors land on the initial doctor dashboard. Administrators land on `/Admin/Index`.
- Doctor dashboard and admin endpoints use server-side role authorization. Patients cannot see review records or forge decisions. The doctor status route always selects by signed-in user ID.
- Approval records are not real-world verification by themselves. The administrator must independently verify identity and licensing, then record the method/reference and explicitly confirm verification. No licensing-registry API or document upload is claimed.

## States and permitted transitions

| Current | Permitted next states |
|---|---|
| No application | Pending (submission) |
| Pending | NeedsInformation, Rejected, Approved (administrator) |
| NeedsInformation / Rejected | Pending (applicant corrects and resubmits) |
| Approved | Suspended (administrator) |
| Suspended | Approved after renewed verification, NeedsInformation, Rejected (administrator) |

Pending/approved/suspended applications cannot be edited by the applicant. Each decision needs a reason. Approval additionally requires complete country/issuer/contact details and the verification checkbox. Self-approval is forbidden even if an account has been granted administrator privileges separately.

SQL Server rowversion detects stale edits and decisions. The request and append-only application audit event are saved in the same SaveChanges transaction. Review events store actor, UTC timestamp, decision, applicant-visible note and a snapshot of the submitted professional fields. There are no public endpoints to update/delete audit events. Administrator accounts are not created through web forms.

## Local administrator setup

The owner selected `osamaaljazar75@gmail.com`. The account must first exist; selecting this email does not automatically grant privileges to anyone registering it.

From `Digital_Pharma` (the web project folder), after creating the intended local account:

```powershell
dotnet bin/Debug/net10.0/Digital_Pharma.dll --environment Development --grant-admin osamaaljazar75@gmail.com
```

This explicitly updates one existing, uniquely matched account. It does not invent a password, seed a default privileged account, or accept an HTTP privilege-escalation request. Only a trusted operator with local deployment access should execute it. Production email verification/recovery is still tracked separately.

## Migration and validation

- Migration `20261109211013_DoctorApprovalWorkflow` adds user flags, application state/contact/country/issuer, rowversion, and DoctorReviews.
- Legacy doctor rows are set to NeedsInformation and IsVerified=false: previous flags have no credential-review audit and must not silently become trusted. Existing account, medication and search records are retained.
- `tests/AccountChecks --prepare-development` backs up the existing local development database into gitignored `.local/backups` before applying migrations. This is a copy-only backup; restoration has not been tested in this batch.
- 63 HTTP/database/password assertions passed on a fresh disposable database with all actual EF migrations. Coverage includes missing details, overposting, unauthorized reads/writes, CSRF, missing verification/decision reason, invalid enum, stale decisions, all review transitions, audit snapshots and role changes on existing sessions.
- No real credential was verified and no test clinician was approved in the owner's development database.
- Remaining scope: evidence uploads/registry integration if adopted, broader account management UI, password recovery/email confirmation, patient links and medication follow-up.

## Owner activation completed — 2026-11-10

The existing account osamaaljazar75@gmail.com was granted administrator access by the local command after the owner completed registration. An independent read after the interrupted session confirmed IsAdmin=true. No password was changed or default account created.
