# The Allocator 1.0.3

This release adds structured telemetry logging and queued upload support for backup and restore activity.

## Highlights

- Writes structured JSONL telemetry alongside existing local logs
- Queues telemetry batches on the backup drive instead of blocking technician workflows
- Attempts log upload automatically at app startup, backup/restore checkpoints, and app exit
- Keeps existing plain-text backup and restore logs intact

## Notes

- Backup and restore continue normally if the logging server is unavailable
- Pending telemetry remains on the drive until a later upload attempt succeeds
