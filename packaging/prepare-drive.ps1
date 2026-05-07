param(
    [Parameter(Mandatory = $true)]
    [string]$DriveLetter,
    [string]$AppFolderName = "The Allocator",
    [string]$BackupsFolderName = "User Backups"
)

$ErrorActionPreference = "Stop"

function Ensure-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        return
    }

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-DriveLetter", "`"$DriveLetter`"",
        "-AppFolderName", "`"$AppFolderName`"",
        "-BackupsFolderName", "`"$BackupsFolderName`""
    )

    $process = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs -PassThru -Wait
    exit $process.ExitCode
}

function New-Shortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$Description
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = Split-Path -Path $TargetPath -Parent
    $shortcut.Description = $Description
    $shortcut.Save()
}

function Remove-DriveContents {
    param(
        [string]$DriveRoot
    )

    Get-ChildItem -LiteralPath $DriveRoot -Force -ErrorAction SilentlyContinue |
        ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
        }
}

Ensure-Administrator

$normalizedDriveLetter = $DriveLetter.Trim().TrimEnd('\', ':')
if ([string]::IsNullOrWhiteSpace($normalizedDriveLetter) -or $normalizedDriveLetter.Length -ne 1) {
    throw "Provide a single drive letter, for example: -DriveLetter E"
}

$driveRoot = "$($normalizedDriveLetter.ToUpper()):\"
if (-not (Test-Path -LiteralPath $driveRoot)) {
    throw "Drive '$driveRoot' was not found."
}

$systemDrive = [Environment]::GetEnvironmentVariable("SystemDrive")
if ($systemDrive -and $driveRoot.StartsWith($systemDrive, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to prepare the system drive: $driveRoot"
}

$payloadRoot = Split-Path -Path $PSScriptRoot -Parent
$appExecutable = Join-Path $payloadRoot "TheAllocator.exe"
if (-not (Test-Path -LiteralPath $appExecutable)) {
    throw "The Allocator payload was not found next to this script. Expected: $appExecutable"
}

$payloadDriveRoot = [System.IO.Path]::GetPathRoot($payloadRoot)
if ($payloadDriveRoot -and $payloadDriveRoot.Equals($driveRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to prepare the same drive that is hosting the installer payload: $driveRoot"
}

$appFolderPath = Join-Path $driveRoot $AppFolderName
$backupsFolderPath = Join-Path $driveRoot $BackupsFolderName
$shortcutPath = Join-Path $driveRoot "The Allocator.lnk"

Write-Host "Preparing drive $driveRoot"
Write-Host "Removing existing contents..."
Remove-DriveContents -DriveRoot $driveRoot

Write-Host "Creating folders..."
New-Item -ItemType Directory -Force -Path $appFolderPath,$backupsFolderPath | Out-Null

Write-Host "Copying The Allocator app files..."
Get-ChildItem -LiteralPath $payloadRoot -Force |
    Where-Object { $_.Name -ne "prepare-drive.ps1" -and $_.Name -ne "prepare-drive.cmd" } |
    ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $appFolderPath -Recurse -Force
    }

Write-Host "Creating top-level shortcut..."
New-Shortcut -ShortcutPath $shortcutPath -TargetPath (Join-Path $appFolderPath "TheAllocator.exe") -Description "Launch The Allocator"

Write-Host "Drive prepared successfully."
Write-Host "App folder: $appFolderPath"
Write-Host "Backups folder: $backupsFolderPath"
