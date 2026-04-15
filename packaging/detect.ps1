$installRoot = "$env:ProgramFiles\Vassar College\The Allocator"
$exePath = Join-Path $installRoot "TheAllocator.exe"

if (-not (Test-Path $exePath)) {
    exit 1
}

$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).FileVersion
if ($version -and $version.StartsWith("1.0.0")) {
    Write-Output "Detected The Allocator $version"
    exit 0
}

exit 1
