# The Allocator 1.0.5

This release improves Windows telemetry conformance, tightens backup handling around OneDrive and Windows app alias placeholders, and cleans up a few technician-facing UI issues.

## Highlights

- Telemetry now sends `platform: windows`
- Backup manifests now store `jobId`, and restore reuses it when available
- Terminal telemetry statuses now use `completed` / `completed_with_warnings`
- Restore telemetry no longer treats `Failed processing 0 files` as an error
- Backup telemetry includes more useful 7-Zip warning summaries
- Backup excludes noisy `WindowsApps` and related placeholder paths that were causing cloud provider backup failures
- Backup destination browse layout is less prone to clipping
- Backup and restore printer lists are denser and easier to scroll
- Machine summary storage now reflects the system drive instead of attached external drives
