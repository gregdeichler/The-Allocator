# Windows Profile Migration Reliability Roadmap

## Purpose

The Allocator's primary Windows job is **reliable user profile migration**.

A Windows migration is not considered successful merely because files were copied or a restore task completed. The actual acceptance standard is:

> **The intended user can sign in to the replacement PC without profile failure and sees their expected files and supported settings retained.**

Everything in this roadmap is subordinate to that requirement. Direct PC-to-PC transfer, external-drive transfer, UI improvements, and performance optimizations are transport or usability features; they must not weaken profile reliability.

---

## Current Architecture to Preserve

The existing repository already contains the right core separation of responsibilities and should be evolved rather than replaced:

- `BackupService.cs` — creates portable user backups.
- `RestoreService.cs` — restores portable profile content and validates the destination.
- `ProfileDiscoveryService.cs` — identifies candidate profiles.
- `WindowsProfileService.cs` — resolves Windows identities/SIDs, creates or validates Windows profiles, and verifies profile registration.
- `PortableProfilePolicy.cs` — defines what profile content is safe and portable.
- `RestoreIdentityPolicy.cs` — protects target identity mapping.
- `TelemetryService.cs` — records migration behavior and outcomes.
- Existing backup/restore XAML pages — provide technician workflow, progress, review, and completion UI.

The current safety model is correct and should remain foundational:

1. Windows owns creation/registration of the destination profile.
2. The Allocator resolves the actual destination account and SID through Windows.
3. Old machine-specific profile hives and credential material are not blindly imported.
4. Portable user data/settings are restored into the Windows-created destination profile.
5. Destination permissions and profile registration are validated.
6. Final migration validation remains pending until the restored user successfully signs in.

---

# Definition of Success

A production Windows migration must meet **all** of the following conditions before it is considered verified:

1. The intended destination account resolves successfully to the expected Windows identity and SID.
2. Windows has a valid registered destination profile for that SID.
3. The user signs in normally.
4. Windows loads the intended profile, not a temporary profile or an unintended `.000`, `.DOMAIN`, or alternate profile.
5. The supported user data set is present after sign-in.
6. Supported settings are retained or intentionally reconstructed.
7. Destination permissions allow the intended user to use the migrated content normally.
8. No supported data is silently omitted.
9. Cloud-backed content is accounted for and unresolved online-only data is clearly reported.
10. Migration exceptions are surfaced to the technician.
11. The Allocator records enough information to audit the migration after the fact.

A migration may have three final states:

- **Verified Success** — sign-in and migration validation both passed with no unresolved supported-data failures.
- **Attention Required** — sign-in succeeded but one or more supported items require technician attention.
- **Failed** — profile creation, identity mapping, restore, sign-in, or required validation failed.

The UI must never present an unresolved migration as an unconditional success.

---

# Phase 1 — Formalize the Windows Migration Contract

## Goal

Define exactly what The Allocator promises to migrate and validate.

## Work

Create a documented migration manifest that separates:

### Core user data

- Desktop
- Documents
- Downloads
- Pictures
- Music
- Videos
- Favorites/bookmarks where applicable
- User-created shortcuts

### Supported application/user settings

Explicitly define and test supported migrations such as:

- Microsoft Edge profile data that is portable and safe
- Google Chrome profile data that is portable and safe
- Firefox profile data that is portable and safe
- Outlook signatures
- Office templates
- Office custom dictionaries
- Other approved roaming application data
- Wallpaper and selected shell preferences where practical
- Printer selections already handled by the existing backup/restore workflow

### Explicitly non-portable or unsafe content

Continue excluding or carefully handling items such as:

- `NTUSER.DAT` and `NTUSER.*`
- `UsrClass.dat*`
- machine-specific registry state
- Windows credential stores
- DPAPI/Crypto/Protect/Vault data that cannot safely move across identities/machines
- `AppData\Local` and `AppData\LocalLow` unless a specific application migration is deliberately added and tested
- caches and temporary data
- reparse points/junction loops
- machine-specific application state

## Deliverable

A versioned migration contract that can be used by backup, restore, validation, tests, and documentation.

---

# Phase 2 — Harden Profile and Identity Discovery

## Goal

Make source and destination identity selection deterministic.

## Work

- Continue resolving the destination account through Windows instead of trusting backup metadata.
- Validate selected existing-profile SID against the resolved destination SID.
- Record source username, source SID, source machine, destination account, destination SID, and destination profile path.
- Detect ambiguous or duplicate local/domain account naming before restore.
- Refuse to restore into the currently signed-in technician profile.
- Detect stale `.bak` ProfileList registrations and handle them only through explicit profile preparation logic.
- Detect unexpected profile paths and fail safely instead of guessing.

## Acceptance tests

- Local account to local account.
- Domain account to domain account.
- Existing destination profile.
- Fresh destination profile.
- Same username with different SID.
- Stale profile registration.
- Existing unexpected `C:\Users\username.*` directory.
- Destination account unreachable/unresolvable.

---

# Phase 3 — Profile Creation Must Remain Windows-Owned

## Goal

Guarantee that the migrated user receives a Windows-valid profile that can load at sign-in.

## Work

Continue using the existing `WindowsProfileService` model:

- Resolve the destination SID through Windows.
- Use the Windows profile APIs for fresh profile creation.
- Validate `ProfileList` registration.
- Validate the expected profile directory.
- Validate that the destination-created `NTUSER.DAT` exists.
- Never replace the destination-created profile hive with the source hive.
- Never import source SID registration as destination truth.

## Hard requirement

The Allocator must not copy an old Windows profile wholesale over a freshly created destination profile.

That shortcut is incompatible with the primary reliability goal.

---

# Phase 4 — Strengthen Backup Completeness

## Goal

Ensure the source package contains every supported item required to recreate the user's working environment.

## Work

- Enumerate supported profile content before backup.
- Record expected file counts and byte counts by top-level migration component.
- Record skipped paths and why they were skipped.
- Distinguish policy exclusions from unexpected access/copy failures.
- Detect encrypted files, inaccessible files, locked files, and unsupported filesystem constructs.
- Detect OneDrive/cloud placeholders and record hydration state.
- Include the migration-contract version in backup metadata.
- Record source OS and profile information already used by the current manifest/telemetry system.

## Rule

A file omitted because policy says it is non-portable is not a failure.

A file omitted unexpectedly is a failure or warning and must be visible to the technician.

---

# Phase 5 — OneDrive and Cloud-Backed Data

## Goal

Prevent online-only placeholders from creating the appearance of a complete migration when the source bytes were never available.

## Work

- Detect OneDrive known-folder redirection.
- Detect placeholder/offline attributes where possible.
- Categorize cloud content as:
  - locally available and backed up,
  - intentionally cloud-resident and expected to rehydrate after sign-in,
  - unresolved/unavailable.
- Record cloud-state counts in the manifest and migration summary.
- Do not claim byte-for-byte local completeness for content that is intentionally cloud-resident.
- Provide a clear technician warning when required content cannot be hydrated or validated.

## Acceptance condition

The user must either see their content locally after migration or have a known, validated cloud-rehydration path after sign-in.

---

# Phase 6 — Restore Engine Reliability

## Goal

Make restore deterministic, resumable, auditable, and incapable of silently dropping supported data.

## Work

Build on the existing `RestoreService` rather than replacing it.

- Preserve preflight archive validation.
- Preserve free-space validation.
- Preserve Windows profile preparation before data restore.
- Preserve base-permission preparation.
- Track restore success/failure by migration component.
- Record copied files and bytes against the backup manifest.
- Detect extraction/copy warnings and promote unresolved supported-data failures to the final status.
- Add restart/resume checkpoints where practical.
- Keep restore idempotent enough that a technician can safely retry after an interrupted operation.

## Performance rule

Performance improvements are welcome only when they do not reduce verification or correctness.

---

# Phase 7 — Destination ACL and Ownership Validation

## Goal

Ensure that copied files are actually usable by the destination user.

## Work

- Validate access using the destination SID.
- Ensure expected inheritance from the destination profile.
- Preserve required `SYSTEM` and Administrators access.
- Do not carry forward unusable source-machine ownership as the destination security model.
- Detect unsupported EFS-encrypted files and report them explicitly.
- Validate key migrated folders after restore.

## Acceptance test

The migrated user must be able to open, modify, create, rename, and delete files in their migrated working folders after sign-in.

---

# Phase 8 — Supported Settings Migration

## Goal

Move beyond file presence to a recognizable working environment without endangering profile reliability.

## Principle

Settings migration must be **application-specific and tested**, not a blind copy of all `AppData`.

## Work

Create individual settings migrators/policies for supported applications and Windows user settings.

Each settings component should define:

- source paths/registry values,
- destination behavior,
- exclusions,
- validation method,
- supported application versions,
- whether the user must sign in to the application again.

Potential initial targets:

- Outlook signatures
- Office templates/custom dictionaries
- browser bookmarks/profile settings
- selected Windows Explorer preferences
- wallpaper
- user-created shortcuts

Add additional applications only after repeatable testing.

---

# Phase 9 — First-Login Verification

## Goal

Make successful user sign-in part of the migration, not an informal technician assumption.

## Required validation

After restore/reboot and user sign-in, verify at minimum:

- the intended SID is signed in,
- the expected profile path is loaded,
- Windows is not using a temporary profile,
- the destination `ProfileList` registration still matches,
- Desktop resolves and is accessible,
- Documents resolves and is accessible,
- Downloads resolves and is accessible,
- migrated content is visible,
- destination permissions work under the user's token,
- supported settings validations pass,
- no critical restore exceptions remain.

## UX target

The restore process should end in a **Pending Login Verification** state rather than final success.

After the user signs in and validation passes, the migration becomes **Verified Success**.

---

# Phase 10 — Verification and Reconciliation

## Goal

Prove that the supported source state and destination state match closely enough to satisfy the migration contract.

## Work

Compare source manifest versus destination results using:

- supported file count,
- supported total byte count,
- component-level counts,
- explicit missing-file list,
- copy/extract errors,
- policy-excluded counts,
- cloud-placeholder counts,
- settings-component validation results.

For high-value or suspicious failures, support stronger per-file verification where useful.

## Final rule

The Allocator must never report **Verified Success** while an unresolved supported-data failure remains.

---

# Phase 11 — Failure Handling and Recovery

## Goal

Make failures recoverable and understandable to technicians.

## Work

Handle and test:

- power loss/reboot,
- cable/network interruption,
- archive interruption,
- destination disk full,
- source file locked,
- source file inaccessible,
- antivirus interference,
- very long paths,
- Unicode/special filenames,
- large files,
- hundreds of thousands of small files,
- profile unexpectedly loaded,
- stale profile registration,
- EFS-encrypted files,
- cloud-only files,
- corrupt backup/archive,
- settings migration failure.

Every failure should produce:

1. a human-readable technician message,
2. a detailed log/telemetry record,
3. a clear retry/recovery path where possible.

---

# Phase 12 — Permanent Migration Torture-Test Profile

## Goal

Prevent regressions.

Maintain a repeatable lab profile containing deliberately difficult content:

- hundreds of thousands of files,
- very large files,
- deep directory trees,
- Unicode and unusual filenames,
- common browser profiles,
- Office/Outlook settings,
- OneDrive local and online-only content,
- junctions/reparse points,
- hidden/system files,
- locked files,
- unusual ACLs,
- unsupported EFS content,
- application settings covered by the migration contract.

Every production release that changes backup, restore, profile handling, identity handling, permissions, or settings migration must pass this profile-migration test.

The test is successful only after the destination user signs in and post-login validation passes.

---

# Phase 13 — Direct PC-to-PC Transfer

## Goal

Add platform parity with macOS while preserving the same Windows migration engine and reliability guarantees.

## Architectural rule

**PC-to-PC transfer is a transport, not a second profile-migration implementation.**

The same backup/restore/profile/validation policies must apply whether the source is:

- external storage, or
- another Windows PC connected directly.

## Preferred transport

Use direct Ethernet between the two PCs, including USB-C/USB Ethernet adapters where needed.

Avoid depending on ordinary USB-C-to-USB-C host connections as the universal Windows transport because support is hardware-dependent.

## Target technician workflow

1. Connect old PC and new PC with the approved Ethernet transfer kit.
2. Launch The Allocator on both systems.
3. Source PC enters **Send Profile** mode.
4. Destination PC enters **Receive Profile** mode.
5. Systems establish a direct transfer link.
6. Technician pairs the systems using a short pairing code.
7. Destination displays discovered source users.
8. Technician selects the source user and target identity.
9. The normal migration engine performs backup/transfer/restore/validation.
10. Source-side temporary networking/sharing/firewall configuration is removed.
11. Destination reaches **Pending Login Verification**.
12. User sign-in completes migration validation.

## Network behavior

The Allocator should automate direct-link networking so technicians do not manually configure adapters.

Example isolated addressing:

- Source: `192.168.77.1/24`
- Destination: `192.168.77.2/24`

Before changing adapter state, record existing configuration and restore it afterward.

## Security

- Do not expose `C:\Users` broadly.
- Use a temporary migration endpoint/share/session.
- Require pairing before data transfer.
- Scope firewall changes to the transfer session/interface where possible.
- Remove temporary shares, credentials, rules, and addressing after completion/failure.

## Initial implementation recommendation

For the first release, prioritize reliability over streaming optimization:

1. Run the existing backup process on the source.
2. Transfer the validated Allocator package directly to the destination.
3. Run the existing restore process on the destination.
4. Run the same post-restore and post-login validation.

Once this path is proven reliable, evaluate streaming/direct restore optimizations without changing the migration contract.

---

# Phase 14 — External-Drive and Direct-Transfer Parity

## Goal

Ensure that transport choice does not change migration quality.

Both transports must use the same:

- migration contract,
- portable-profile policy,
- source profile discovery,
- identity validation,
- restore engine,
- ACL repair/validation,
- settings migration,
- logging/telemetry,
- completion states,
- first-login verification.

A technician should choose a transport, not a different migration implementation.

---

# Phase 15 — Technician UX

## Goal

Keep complexity inside The Allocator.

Target migration entry screen:

- **Direct Computer Transfer**
- **External Drive**
- **No User Data Transfer**

For either transfer mode, present the same high-level stages:

1. Select source user.
2. Select/confirm destination user.
3. Preflight validation.
4. Transfer/restore.
5. Data/settings verification.
6. Reboot/sign-in.
7. Login verification.
8. Verified completion or actionable exception summary.

Do not require technicians to understand SIDs, `ProfileList`, temporary shares, Robocopy/7-Zip internals, adapter addressing, or ACL implementation details.

---

# Phase 16 — Telemetry and Supportability

## Goal

Make every migration diagnosable after the fact.

Continue using the existing telemetry/logging framework and expand it to capture:

- job ID,
- Allocator version,
- migration-contract version,
- source computer,
- destination computer,
- source OS,
- destination OS,
- source username/SID,
- destination account/SID,
- destination profile path,
- transport type,
- start/end time,
- source supported file/byte counts,
- destination verified file/byte counts,
- policy exclusions,
- unexpected failures,
- cloud placeholder state,
- settings-component results,
- ACL validation result,
- profile registration result,
- login verification result,
- final migration status.

Logs should make it possible to answer: **What was expected to migrate, what actually migrated, what did not, why, and did the user successfully load the intended profile?**

---

# Implementation Order

The order below is deliberate. Profile reliability comes before transport parity.

1. Formalize/version the Windows migration contract.
2. Harden source/destination profile and SID discovery.
3. Expand backup manifest completeness/error reporting.
4. Add OneDrive/cloud-state handling.
5. Harden restore verification and retry behavior.
6. Harden ACL/ownership validation.
7. Implement explicitly supported settings migrators.
8. Implement first-login verification and final migration states.
9. Build source-versus-destination reconciliation.
10. Build the permanent torture-test profile and release gate.
11. Add direct PC-to-PC transport using the proven backup/restore engine.
12. Align direct-transfer and external-drive UX.
13. Stress-test interruptions and recovery paths.
14. Pilot with technicians.
15. Release only after real login verification consistently succeeds.

---

# Release Gate

A Windows Allocator release that changes migration behavior should not be considered production-ready until it demonstrates the following on representative test machines:

- source profile discovered correctly,
- target identity/SID resolved correctly,
- destination profile created/validated by Windows,
- supported files restored,
- supported settings restored,
- unsupported content explicitly excluded rather than silently lost,
- permissions validated,
- no temporary-profile condition,
- no unintended alternate profile path,
- destination user signs in successfully,
- expected files are visible to the user,
- supported settings are visible/functional,
- post-login validation succeeds,
- telemetry/logging contains a complete migration record.

**The final test is the user's successful sign-in and usable retained profile.**

---

## Guiding Principle

> **Profile migration is the product. Transport is implementation detail.**

The Allocator should optimize for the moment when the user sits down at the replacement Windows PC, signs in successfully, and finds their files and supported working environment where they expect them.
