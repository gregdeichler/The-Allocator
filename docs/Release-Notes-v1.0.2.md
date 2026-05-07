# The Allocator 1.0.2

Focused backup and deployment utility hardening release.

## Included In This Release

- backup scan now skips inaccessible folders instead of failing the whole backup
- restore logs suppress known non-fatal legacy junction and compatibility-link noise
- restored file counting now ignores inaccessible and reparse-point paths more cleanly
- added a technician-facing external drive prep utility
- added drive prep scripts to wipe a selected external drive at the file level, copy The Allocator, create a top-level shortcut, and create a `User Backups` folder

## Why This Release Exists

Testing exposed a backup failure caused by an unreadable CrashPlan folder inside a user profile. One inaccessible application folder should not stop a technician from completing a backup. This release makes the backup scan tolerant of those paths and packages the current drive-prep utility work so technicians can prepare external migration drives more easily.

## Testing Focus

1. Re-run the previously failing backup and confirm the inaccessible CrashPlan folder is skipped instead of aborting the backup.
2. Confirm restore logs are quieter and no longer scare technicians with non-fatal compatibility-link noise.
3. Test the drive prep utility on a disposable external drive and confirm it creates:
   - `The Allocator`
   - `User Backups`
   - `The Allocator.lnk`
