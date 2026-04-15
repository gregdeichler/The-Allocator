<p align="left">
  <img src="Assets/Vassar_Wordmark_VassarBurgundy_RGB.png" alt="Vassar College wordmark" width="420">
</p>

# The Allocator

**The Allocator** is a Windows desktop migration utility built for Vassar College. It gives technicians a guided, repeatable way to move a user from an old or reimaged computer to a working Windows profile on the destination machine.

## At A Glance

- Guided **Old Computer** and **New Computer** workflows
- Per-user backup packages with sidecar metadata and printer definitions
- Same-user overwrite restore path validated in real testing
- Built-in reboot step after restore
- Vassar-specific assumptions for technician use, profile repair, and printer handling

## What The App Does

The current application supports:

- selecting a Windows user profile to back up
- capturing selected printers and profile metadata
- creating a portable backup package on external storage
- restoring that package onto a destination machine
- reconnecting the restored profile to the target Windows account
- applying profile permissions needed for successful sign-in

Current backup package layout:

- `<selecteduser>\<selecteduser>-backup.7z`
- `<selecteduser>\<selecteduser>-backup.json`
- `<selecteduser>\<selecteduser>-printers.json`
- `<selecteduser>\<selecteduser>-backup-log.txt`
- `<selecteduser>\<selecteduser>-restore-log.txt`

## Current Status

The Allocator has successfully completed real same-user migration tests:

- backup completes successfully
- same-user overwrite restore completes successfully
- restored users can sign in after reboot

Still being actively hardened:

- printer recreation across more device and driver combinations
- merge restore behavior for already-existing healthy profiles
- broader multi-technician and multi-machine testing

## Technician Workflow

### Old Computer

1. Launch `TheAllocator.exe` as Administrator.
2. Choose `Old Computer`.
3. Select the user profile to back up.
4. Select the printers to include.
5. Choose the destination drive or folder.
6. Review and begin backup.

### New Computer

1. Sign in with a different administrator account such as `usadmin`.
2. Launch `TheAllocator.exe` as Administrator.
3. Choose `New Computer`.
4. Select the backup package.
5. Select the target account.
6. For same-user rebuild testing, choose `Overwrite`.
7. Complete the restore and use the app’s reboot action.
8. Sign in to the restored account after reboot.

## Build

From PowerShell:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' publish '.\TheAllocator.csproj' -c Release -r win-x64 --self-contained true -o '.\release\TheAllocator-test'
```

Published executable:

- `release\TheAllocator-test\TheAllocator.exe`

## Tech Stack

- .NET 10 WPF desktop app
- bundled 7-Zip command-line tooling for archive creation and extraction
- Windows-native profile, ACL, registry, and printer operations

## Repository Scope

This repository is for **The Allocator only**.

It should not include files, assets, installers, or release artifacts from:

- Asset Tool
- AllocationUtility
- Migration Utility
- older USMT or Transwiz experiments

## Internal App Identity

- Company: `Vassar College`
- Product: `The Allocator`
- Assembly: `TheAllocator`
- Startup window: `NavigatorWindow.xaml`

## Release Notes

- `docs\Release-Notes-v1.0.0.md`
