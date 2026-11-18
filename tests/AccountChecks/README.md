# Account acceptance checks

Run from the repository root on Windows with .NET and SQL Server LocalDB installed:

```powershell
dotnet run --project tests/AccountChecks -p:MSBuildEnableWorkloadResolver=false
```

The runner creates a uniquely named `DigitalPharma_AccountChecks_*` database, applies the application's real EF migrations, starts the app on an available loopback port, exercises HTTP requests with cookies and antiforgery tokens, and removes that test database in `finally`. It never uses the application's configured database for these integration tests. All account data is synthetic. A failed run writes `last-run.log` (gitignored). A forcibly interrupted runner may require cleanup of its exact test database.

The original account assertions cover password salts and verification, legacy SHA-256 upgrades, invalid hashes, application startup, anonymous access, CSRF rejection, registration validation, normalized/duplicate emails, cookie attributes, account rendering, medication ownership, doctor overposting, logout semantics, incorrect/correct login, locked accounts and request limits. This is a standalone assertion runner with a nonzero exit code on failure; it requires no additional test-framework packages.

Additional modes:

```powershell
dotnet run --project tests/AccountChecks -- --passwords-only
dotnet run --project tests/AccountChecks -- --prepare-development
dotnet run --project tests/AccountChecks -- --probe
dotnet run --project Digital_Pharma --launch-profile http
```

`--prepare-development` applies migrations only to the fixed, separate local database `DigitalPharma_Development`. It does not delete or reset that database. `--probe` reads the base/development connection setting and checks connectivity and the Users schema without printing records. The application itself still honors environment-variable configuration overrides.

## Result: 2026-11-08

- Build passed; 31 assertions passed against a disposable database created with real migrations.
- Development database prepared; connection and Users query passed.
- Local development site started at http://localhost:5170.
- No external AI/FDA calls or visual browser acceptance tests were performed.
- Existing nullable warnings in Doctor and SharedResult remain outside this change. NuGet vulnerability auditing was unavailable because the package endpoint was inaccessible; this does not establish that packages are vulnerability-free.

## Scope and remaining work

- Authentication uses ASP.NET Core encrypted/signed cookies and PasswordHasher PBKDF2 hashes. Production cookies require HTTPS; local HTTP is development-only.
- The old Password database column remains for schema compatibility. New accounts store an empty value. Existing SHA-256 accounts upgrade and erase their plaintext value on successful login. This is not a bulk cleanup of dormant legacy accounts or historical backups; a controlled migration is needed when an existing real database is connected.
- Existing session-only logins must sign in again after this change.
- Account endpoints share 10 requests per IP per minute. This in-memory limit is intended for the current single-process deployment. Distributed deployments and trusted proxy forwarding need separate configuration.
- Doctor approval workflows/roles were added on 2026-11-10 (see below). Password reset, email confirmation, durable per-account failed-login lockout and security-stamp based session revocation remain future work. The existing LockoutEnd field is enforced, including for an already-issued cookie.
- Doctor requests use a dedicated input model for professional fields; clients cannot set IsVerified, Status, UserId, IsAdmin or Id. Real-world credential verification is a manual administrative responsibility.
- Sharing links remain deliberately public bearer links as in the existing product; expiry/revocation and full privacy review are tracked separately.
- Production configuration still requires a valid database connection and service secrets supplied securely.


## Result: 2026-11-10 — doctor approval

63 assertions passed on the full disposable-database test run. The additional assertions exercise patient/applicant/doctor/admin boundaries; application completeness; role and ownership overposting; applicant routing; pending-edit rejection; administrator credential confirmation and decision reasons; invalid decision enums; stale versions; request for information, correction, rejection, resubmission, approval, suspension, reinstatement; eight preserved submission/decision events with actor/time/snapshot; immediate cookie role changes; and signup intent without privilege escalation.

Migration generation and build succeeded. A copy-only development database backup was created before applying the new migration. The initial owner admin grant returned ADMIN_NOT_ASSIGNED because the specified account did not yet exist; no default admin account/password was created. See DOCTOR_WORKFLOW.md for the local provisioning command.

Visual check covered the registration page and improved account-type field styling. Professional pages were verified through HTTP rendering and authorization tests; broader visual/mobile review is still outstanding. No real physician credentials or external medical APIs were tested. The earlier nullable warning in SharedResult and unavailable NuGet vulnerability audit remain.


Linked health checks: 90 assertions passed on 2026-11-10 using `--live-integration` (88 without it). `--live-label` independently checks public FDA search and exact-ID retrieval. Both live modes require network access; no patient profile is sent to FDA. See ../../LINKED_HEALTH.md for coverage and limitations.

Medication cards (2026-11-10): standard suite now passes 115 assertions. New coverage includes owner-only edits, CSRF, forged fields, stale rowversions, decimal precision, paired units, invalid/duplicate daily times, as-needed instructions, zero stock, atomic audit snapshots, and stop/resume effects on current medication reviews. `--live-integration` remains optional and was not rerun in this card-only change.

Daily doses (2026-11-11): standard suite passes 140 assertions. Includes simultaneous submissions, patient isolation, CSRF, future-date/time rejection, stale card/correction rejection, required correction reasons, undo with retained audit, snapshot preservation, and history retained after medication deletion. Uses Asia/Amman for schedule dates. No live midnight rollover or visual/mobile checks were performed.

Patient-doctor consent (2026-11-11): standard suite passes 166 assertions, including 26 new checks for explicit consent, pending/active/rejected/revoked states, suspension, role and ownership isolation, stale transitions, read-only access, no-store response headers and consent event history. No links are seeded in the development database.

Navigation/login diagnostics (2026-11-11): 170 standard assertions passed. `--diagnose-account email` reports only existence count, credential format category, lock status, legacy plaintext presence and email whitespace against development configuration. It does not validate the user-entered password or modify accounts. Never print hashes or request passwords in chat.

Doctor treatment changes (completed 2026-11-12): 200 checks passed with `--live-treatment` (198 without live source addition). Covers separate patient treatment consent, scoped edits, CSRF, medication/link versions, idempotency, preserved inventory and personal notes, atomic audit/notification writes, notification privacy/read acknowledgement, stop/archive and withdrawal/suspension/revocation. Synthetic HTML previews are exported under ignored `.local/treatment-preview`; CSS is copied for layout review only. No production login bypass exists. The three exported pages were inspected at 390px and 1280px; no anchor/button intersections or horizontal overflow were observed.
