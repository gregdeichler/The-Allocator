param(
    [string]$InstallRoot = "$env:ProgramFiles\Vassar College\The Allocator",
    [string]$Version = "1.0.0"
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
        "-InstallRoot", "`"$InstallRoot`"",
        "-Version", "`"$Version`""
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

Ensure-Administrator

$appArchive = Join-Path $PSScriptRoot "app.zip"
if (-not (Test-Path $appArchive)) {
    throw "Application payload archive is missing: $appArchive"
}

if (Test-Path $InstallRoot) {
    Get-ChildItem -Path $InstallRoot -Force | Remove-Item -Recurse -Force
}
else {
    New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
}

Expand-Archive -Path $appArchive -DestinationPath $InstallRoot -Force
Copy-Item -Path (Join-Path $PSScriptRoot "uninstall.ps1") -Destination (Join-Path $InstallRoot "uninstall.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "detect.ps1") -Destination (Join-Path $InstallRoot "detect.ps1") -Force

$programsFolder = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\Vassar College"
New-Item -ItemType Directory -Force -Path $programsFolder | Out-Null
New-Shortcut -ShortcutPath (Join-Path $programsFolder "The Allocator.lnk") -TargetPath (Join-Path $InstallRoot "TheAllocator.exe") -Description "The Allocator"

$uninstallKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TheAllocator"
New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name DisplayName -Value "The Allocator"
Set-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value $Version
Set-ItemProperty -Path $uninstallKey -Name Publisher -Value "Vassar College"
Set-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $InstallRoot
Set-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value (Join-Path $InstallRoot "TheAllocator.exe")
Set-ItemProperty -Path $uninstallKey -Name UninstallString -Value "powershell.exe -ExecutionPolicy Bypass -File `"$InstallRoot\uninstall.ps1`""
Set-ItemProperty -Path $uninstallKey -Name QuietUninstallString -Value "powershell.exe -ExecutionPolicy Bypass -File `"$InstallRoot\uninstall.ps1`" -Quiet"
Set-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -Type DWord

Write-Host "The Allocator $Version installed to $InstallRoot"
