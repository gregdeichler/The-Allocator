# The Allocator 1.0.4

This release hardens the new telemetry integration and the drive prep utility, and updates backup wording so the UI matches the actual file layout.

## Highlights

- Telemetry batches are now written incrementally during backup and restore
- Successful telemetry uploads are confirmed through the drive-level queue and status log
- Drive Prep Utility is now compatible with Windows PowerShell 5.1 and no longer crashes when capturing process output
- Backup destination and review screens now reflect the real files and folders created on the drive

## Notes

- Per-user structured telemetry logs are written inside each backup folder under `logs`
- Drive-level telemetry queues live under `Telemetry\pending`, `Telemetry\sent`, and `Telemetry\failed`
