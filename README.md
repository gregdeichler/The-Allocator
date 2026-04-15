# The Allocator

The Allocator is a Windows desktop migration utility built for Vassar College. It is designed to let technicians back up a user profile from an old computer and restore that same user to a reimaged or replacement Windows computer through a guided, technician-friendly workflow.

## What It Does

The current application supports:

- selecting a Windows user profile to back up
- capturing selected printers and profile metadata
- creating a portable backup package on external storage
- restoring that package onto a destination machine
- reconnecting the restored profile to the target Windows account
- applying profile permissions needed for successful sign-in

The current backup package layout is:

- `<selecteduser>\<selecteduser>-backup.7z`
- `<selecteduser>\<selecteduser>-backup.json`
- `<selecteduser>\<selecteduser>-printers.json`
- `<selecteduser>\<selecteduser>-backup-log.txt`
- `<selecteduser>\<selecteduser>-restore-log.txt` after restore

## Current Testing Status

The Allocator has successfully completed real-world same-user migration tests:

- backup completes successfully
- same-user overwrite restore completes successfully
- the restored user can sign in after reboot

Areas still being actively validated:

- printer recreation across more device and driver combinations
- merge restore behavior against already-existing healthy profiles
- broader technician testing across multiple machines

## Tech Stack

- .NET 10 WPF desktop app
- bundled 7-Zip command-line tooling for archive creation and extraction
- Windows-native profile, ACL, registry, and printer operations

## Running The App

1. Launch `TheAllocator.exe` as Administrator.
2. Choose:
   - `Old Computer` to create a backup
   - `New Computer` to restore a backup
3. Follow the guided steps in the application.

For same-user full profile rebuild testing, the validated path is:

1. Sign into the destination machine with a different admin account such as `usadmin`.
2. Run a restore for the same user.
3. Use `Overwrite` for a clean same-user profile rebuild.
4. Reboot from the restore completion screen.
5. Sign into the restored account after reboot.

## Build

From PowerShell:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' publish '.\TheAllocator.csproj' -c Release -r win-x64 --self-contained true -o '.\release\TheAllocator-test'
```

Published executable:

- `release\TheAllocator-test\TheAllocator.exe`

## Repository Scope

This repository is for **The Allocator only**.

It should not contain files, assets, installers, or release artifacts from:

- Asset Tool
- AllocationUtility
- Migration Utility
- earlier USMT/Transwiz experiments

## Internal Notes

- Company: Vassar College
- Product: The Allocator
- Assembly: `TheAllocator`
- Startup window: `NavigatorWindow.xaml`

## Release Notes

Initial release notes are in:

- `docs\Release-Notes-v1.0.0.md`
