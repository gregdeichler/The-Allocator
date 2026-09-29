# The Allocator 2.0.0

This release replaces the production presentation layer with a WinUI 3 technician console while preserving the version 1.1 backup package, restore policy, telemetry, printer handling, elevation, and installation path.

## Technician experience

- Consolidates both computer roles into Setup, Review, Transfer, and Finish.
- Adds structured readiness checks before backup or restore begins.
- Replaces raw progress output with named phases, activity milestones, and trustworthy determinate progress only when totals are available.
- Adds native Fluent controls, restrained Vassar branding, Mica on Windows 11, Windows 10 visual fallbacks, and contrast-theme resources.
- Adds keyboard access keys, screen-reader names, live progress announcements, scalable layouts, and persistent window bounds.
- Adds structured completion actions for opening backup folders, viewing logs, and confirming a reboot.

## Compatibility

- Existing `.allocator.7z` and `.7z` packages remain supported.
- Backup manifest fields, sidecar filenames, telemetry, portable-profile policy, ACL repair, and collision behavior are unchanged.
- The application remains an elevated, unpackaged, self-contained x64 deployment at the existing install location.
