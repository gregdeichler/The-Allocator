# The Allocator 1.1.0

This test release rebuilds profile restore around Windows-supported profile creation and a portable-data migration policy.

## Restore Reliability

- Windows now creates fresh target profiles through the Windows User Profile API
- Existing target profiles must be registered, complete, and not loaded
- Manual `ProfileList` registry binding has been removed
- Source `NTUSER.DAT` and `UsrClass.dat` files are never restored
- `AppData\Local`, `AppData\LocalLow`, Windows shell state, credentials, and DPAPI material are never restored
- Same-user restores default to a Windows-created fresh profile
- Domain account SID and account type are validated before profile changes begin
- The Windows profile registration and destination hive are validated before the file restore is marked complete

## Backup Policy

- New backups contain portable user folders and `AppData\Roaming`
- Windows profile hives and machine-specific application state are excluded
- Existing 1.0.x archives remain readable and are filtered by the safer restore policy
- Backup manifests identify migration policy version 2

## Printers And Status

- Shared printer connections are installed per computer instead of into the technician account
- Default-printer selection is deferred until the restored user signs in
- Completion wording now states that user sign-in validation is still pending

## Required Test

Run the first test on a clean reimaged destination computer from `usadmin`. Use a same-user domain backup, reboot when prompted, and confirm the user can load the desktop before testing files and printers.
