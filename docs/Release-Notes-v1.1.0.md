# The Allocator 1.1.0

The Allocator 1.1.0 is the official restore-reliability release. It rebuilds profile restore around Windows-supported profile creation, a portable-data migration policy, stricter identity validation, safer printer recreation, and automated regression coverage.

## Restore Reliability

- Windows creates fresh target profiles through the Windows User Profile API.
- Existing target profiles must be registered, complete, and not loaded.
- Manual `ProfileList` registry binding has been removed.
- Source `NTUSER.DAT` and `UsrClass.dat` files are never restored.
- `AppData\Local`, `AppData\LocalLow`, Windows shell state, credentials, and DPAPI material are never restored.
- Same-user restores default to a Windows-created fresh profile.
- Target accounts are resolved independently through Windows; backup SIDs are validation evidence, not trusted identity input.
- Existing-profile restores require the selected profile SID to match the independently resolved Windows account SID.
- Domain account SID and account type are validated before profile changes begin.
- Windows profile registration and the destination hive are validated before the file restore is marked complete.

## Backup Policy

- New backups contain portable user folders and `AppData\Roaming`.
- Windows profile hives and machine-specific application state are excluded.
- Existing 1.0.x archives remain readable and are filtered by the safer restore policy.
- Backup manifests identify migration policy version 2.

## Printers

- Shared printer connections are installed per computer instead of into the technician account.
- TCP/IP printer metadata now preserves the Windows port name and actual host/IP address separately.
- Older backups without `HostAddress` are supported when the address can be inferred safely; ambiguous custom port names are not guessed.
- Required printer restore command failures now propagate into a failed restore instead of allowing a false success.
- Standardized driver installation can still use fallback methods before being treated as failed.
- Default-printer selection is deferred until the restored user signs in.

## Quality Gate

- Windows GitHub Actions CI now builds the real WPF application in Release mode.
- Restore regression checks cover printer host-address handling and legacy metadata, portable-profile exclusions, SID mismatch protection, and printer failure propagation safeguards.
- The release candidate passed both the WPF build and regression suite before release.

## Validation After Deployment

For the first production validation, use a clean reimaged destination computer from `usadmin` with a same-user domain backup. Reboot when prompted and confirm the restored user can load the desktop, then verify migrated files and restored printers.
