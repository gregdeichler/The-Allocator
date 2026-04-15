param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
$releaseRoot = Join-Path $projectRoot "release\$Version"
$appOut = Join-Path $releaseRoot "app"
$appZip = Join-Path $releaseRoot "app.zip"
$installerWork = Join-Path $releaseRoot "installer-work"
$installerOut = Join-Path $releaseRoot "installer"
$sourceOut = Join-Path $releaseRoot "source"
$setupName = "TheAllocator-$Version-Setup.exe"

if (Test-Path $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $releaseRoot,$appOut,$installerWork,$installerOut,$sourceOut | Out-Null

$env:DOTNET_CLI_HOME = Join-Path $projectRoot ".dotnet-cli"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

& "C:\Program Files\dotnet\dotnet.exe" publish (Join-Path $projectRoot "TheAllocator.csproj") -c Release -r win-x64 --self-contained true -o $appOut
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Compress-Archive -Path (Join-Path $appOut "*") -DestinationPath $appZip -Force

Copy-Item -Path (Join-Path $projectRoot "packaging\install.ps1") -Destination $installerWork -Force
Copy-Item -Path (Join-Path $projectRoot "packaging\uninstall.ps1") -Destination $installerWork -Force
Copy-Item -Path (Join-Path $projectRoot "packaging\detect.ps1") -Destination $installerWork -Force
Copy-Item -Path (Join-Path $projectRoot "packaging\install.cmd") -Destination $installerWork -Force
Copy-Item -Path (Join-Path $projectRoot "packaging\uninstall.cmd") -Destination $installerWork -Force
Copy-Item -Path $appZip -Destination (Join-Path $installerWork "app.zip") -Force

$sedTemplate = Get-Content -Path (Join-Path $projectRoot "packaging\installer.sed.template") -Raw
$targetExe = Join-Path $installerOut $setupName
$sedContent = $sedTemplate.Replace("{{SOURCE_DIR}}", $installerWork).Replace("{{TARGET_EXE}}", $targetExe)
$sedPath = Join-Path $installerOut "installer.sed"
Set-Content -Path $sedPath -Value $sedContent -Encoding ASCII

$iexpress = Start-Process -FilePath "C:\Windows\System32\iexpress.exe" -ArgumentList @("/N", $sedPath) -PassThru -Wait
if ($iexpress.ExitCode -ne 0) {
    throw "IExpress installer build failed with exit code $($iexpress.ExitCode)."
}

if (-not (Test-Path $targetExe)) {
    throw "IExpress installer build did not produce the expected setup file: $targetExe"
}

$sourceKeep = @(
    "app.manifest",
    "App.xaml",
    "App.xaml.cs",
    "AssemblyInfo.cs",
    "Build-Release.ps1",
    "NavigatorWindow.xaml",
    "NavigatorWindow.xaml.cs",
    "NuGet.Config",
    "README.md",
    "TheAllocator.csproj",
    "Assets",
    "docs",
    "Models",
    "Pages",
    "Services",
    "tools",
    "packaging"
)

foreach ($item in $sourceKeep) {
    $path = Join-Path $projectRoot $item
    if (Test-Path $path) {
        Copy-Item -Path $path -Destination $sourceOut -Recurse -Force
    }
}

$sourceArchiveName = "TheAllocator-$Version-source.zip"
$appArchiveName = "TheAllocator-$Version-app.zip"
Compress-Archive -Path (Join-Path $sourceOut "*") -DestinationPath (Join-Path $releaseRoot $sourceArchiveName) -Force
Copy-Item -Path $appZip -Destination (Join-Path $releaseRoot $appArchiveName) -Force

@"
The Allocator $Version

Artifacts:
- app\ : self-contained application files
- installer\$setupName : bootstrap installer
- $sourceArchiveName : source archive
- $appArchiveName : portable app archive
"@ | Set-Content -Path (Join-Path $releaseRoot "RELEASE-NOTES.txt") -Encoding UTF8

Write-Host "Release built at $releaseRoot"
