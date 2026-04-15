# The Allocator Plan

## Product goal

Build a modern .NET desktop app that performs user migrations without depending on USMT or Transwiz.

## Backup workflow

1. Detect valid local user profiles.
2. Let the technician select the user and transfer destination.
3. Collect profile metadata:
   - username
   - SID
   - profile path
   - domain or local account details if resolvable
   - printer configuration
4. Build a backup package with:
   - user files and settings captured from the selected profile
   - a restore configuration file
   - logs
5. Compress the package lightly for faster backup and restore.

## Restore workflow

1. Open the backup package from external storage.
2. Read the restore configuration automatically.
3. Restore the profile data to the destination machine.
4. Repair ownership and ACLs on the restored profile.
5. Map the restored profile to the intended user account.
6. Restore printers.
7. Run final validation checks before sign-in.

## Architecture direction

- `Views`
  Technician-first stepper workflow for old-PC and new-PC paths.
- `Models`
  Backup package metadata, printer definitions, restore targets, and validation results.
- `Services`
  Profile discovery, capture engine, compression, printer export/import, account repair, and logging.
- `Storage`
  A single backup artifact plus a JSON configuration file inside it or beside it.

## Open design questions

- What exact parts of `C:\Users\<name>` should be included by default?
- Which application settings matter most in your environment?
- Should the backup package be one archive file or a folder bundle with a primary archive inside it?
- What printer details are required to recreate mappings reliably?
- What domain repair steps are required for your environment after restore?
