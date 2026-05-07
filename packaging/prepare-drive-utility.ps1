Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = "Stop"

function Get-ReadableSize {
    param(
        [long]$Bytes
    )

    if ($Bytes -ge 1GB) { return "{0:N1} GB" -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return "{0:N1} MB" -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return "{0:N1} KB" -f ($Bytes / 1KB) }
    return "$Bytes bytes"
}

function Get-DriveTypeLabel {
    param(
        [uint32]$DriveType
    )

    switch ($DriveType) {
        2 { "Removable" }
        3 { "Fixed" }
        default { "Other" }
    }
}

function Get-CandidateDrives {
    $systemRoot = ([Environment]::GetEnvironmentVariable("SystemDrive") ?? "").TrimEnd('\') + "\"
    $payloadRoot = [System.IO.Path]::GetPathRoot((Split-Path -Path $PSScriptRoot -Parent))

    Get-CimInstance Win32_LogicalDisk |
        Where-Object {
            $_.DriveType -in 2, 3 -and
            $_.Size -gt 0 -and
            $_.DeviceID
        } |
        ForEach-Object {
            $root = "$($_.DeviceID)\"
            if ($root.Equals($systemRoot, [System.StringComparison]::OrdinalIgnoreCase)) { return }
            if ($payloadRoot -and $root.Equals($payloadRoot, [System.StringComparison]::OrdinalIgnoreCase)) { return }

            [pscustomobject]@{
                DeviceID    = $_.DeviceID
                VolumeName  = if ([string]::IsNullOrWhiteSpace($_.VolumeName)) { "(No label)" } else { $_.VolumeName }
                FileSystem  = if ([string]::IsNullOrWhiteSpace($_.FileSystem)) { "Unknown" } else { $_.FileSystem }
                DriveType   = $_.DriveType
                DriveTypeLabel = Get-DriveTypeLabel $_.DriveType
                Size        = [int64]$_.Size
                FreeSpace   = [int64]$_.FreeSpace
                RootPath    = $root
            }
        } |
        Sort-Object DeviceID
}

function Show-ConfirmDialog {
    param(
        [string]$DriveLetter,
        [string]$VolumeName
    )

    $result = [System.Windows.Forms.MessageBox]::Show(
        "This will erase all files and folders on $DriveLetter ($VolumeName), copy The Allocator onto the drive, create a top-level shortcut, and create a User Backups folder.`n`nContinue?",
        "Prepare Drive",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Warning)

    return $result -eq [System.Windows.Forms.DialogResult]::Yes
}

function Invoke-DrivePreparation {
    param(
        [string]$DriveLetter
    )

    $scriptPath = Join-Path $PSScriptRoot "prepare-drive.ps1"
    $logPath = Join-Path ([System.IO.Path]::GetTempPath()) ("allocator-drive-prep-{0}.log" -f [guid]::NewGuid().ToString("N"))

    $process = Start-Process -FilePath "powershell.exe" `
        -ArgumentList @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", $scriptPath,
            "-DriveLetter", $DriveLetter
        ) `
        -PassThru `
        -Wait `
        -RedirectStandardOutput $logPath `
        -RedirectStandardError $logPath

    $output = if (Test-Path -LiteralPath $logPath) {
        Get-Content -Path $logPath -Raw
    } else {
        ""
    }

    if (Test-Path -LiteralPath $logPath) {
        Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
    }

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output   = $output.Trim()
    }
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "The Allocator Drive Prep Utility"
$form.StartPosition = "CenterScreen"
$form.Size = New-Object System.Drawing.Size(860, 560)
$form.MinimumSize = New-Object System.Drawing.Size(860, 560)
$form.BackColor = [System.Drawing.Color]::FromArgb(248, 244, 238)

$titleLabel = New-Object System.Windows.Forms.Label
$titleLabel.Text = "Prepare External Drive"
$titleLabel.Font = New-Object System.Drawing.Font("Segoe UI Semibold", 20)
$titleLabel.ForeColor = [System.Drawing.Color]::FromArgb(99, 27, 43)
$titleLabel.Location = New-Object System.Drawing.Point(24, 20)
$titleLabel.AutoSize = $true
$form.Controls.Add($titleLabel)

$subtitleLabel = New-Object System.Windows.Forms.Label
$subtitleLabel.Text = "Choose the drive you want to erase and configure for The Allocator."
$subtitleLabel.Font = New-Object System.Drawing.Font("Segoe UI", 11)
$subtitleLabel.ForeColor = [System.Drawing.Color]::FromArgb(88, 88, 88)
$subtitleLabel.Location = New-Object System.Drawing.Point(28, 62)
$subtitleLabel.AutoSize = $true
$form.Controls.Add($subtitleLabel)

$instructionPanel = New-Object System.Windows.Forms.Panel
$instructionPanel.Location = New-Object System.Drawing.Point(24, 95)
$instructionPanel.Size = New-Object System.Drawing.Size(804, 80)
$instructionPanel.BackColor = [System.Drawing.Color]::White
$instructionPanel.BorderStyle = "FixedSingle"
$form.Controls.Add($instructionPanel)

$instructionLabel = New-Object System.Windows.Forms.Label
$instructionLabel.Text = "What this utility does:`n- Erases all current files and folders on the selected drive`n- Copies The Allocator onto the drive`n- Creates a top-level shortcut and a User Backups folder"
$instructionLabel.Font = New-Object System.Drawing.Font("Segoe UI", 10)
$instructionLabel.ForeColor = [System.Drawing.Color]::FromArgb(55, 55, 55)
$instructionLabel.Location = New-Object System.Drawing.Point(14, 12)
$instructionLabel.Size = New-Object System.Drawing.Size(760, 54)
$instructionPanel.Controls.Add($instructionLabel)

$driveList = New-Object System.Windows.Forms.ListView
$driveList.Location = New-Object System.Drawing.Point(24, 194)
$driveList.Size = New-Object System.Drawing.Size(804, 220)
$driveList.View = "Details"
$driveList.FullRowSelect = $true
$driveList.MultiSelect = $false
$driveList.HideSelection = $false
$driveList.GridLines = $true
$driveList.Font = New-Object System.Drawing.Font("Segoe UI", 10)
[void]$driveList.Columns.Add("Drive", 80)
[void]$driveList.Columns.Add("Label", 210)
[void]$driveList.Columns.Add("Type", 100)
[void]$driveList.Columns.Add("File System", 100)
[void]$driveList.Columns.Add("Size", 130)
[void]$driveList.Columns.Add("Free", 130)
$form.Controls.Add($driveList)

$detailsLabel = New-Object System.Windows.Forms.Label
$detailsLabel.Text = "Select a drive to see details."
$detailsLabel.Font = New-Object System.Drawing.Font("Segoe UI", 10)
$detailsLabel.ForeColor = [System.Drawing.Color]::FromArgb(70, 70, 70)
$detailsLabel.Location = New-Object System.Drawing.Point(28, 430)
$detailsLabel.Size = New-Object System.Drawing.Size(540, 50)
$form.Controls.Add($detailsLabel)

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.Text = ""
$statusLabel.Font = New-Object System.Drawing.Font("Segoe UI", 10)
$statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(99, 27, 43)
$statusLabel.Location = New-Object System.Drawing.Point(28, 478)
$statusLabel.Size = New-Object System.Drawing.Size(540, 24)
$form.Controls.Add($statusLabel)

$refreshButton = New-Object System.Windows.Forms.Button
$refreshButton.Text = "Refresh Drives"
$refreshButton.Location = New-Object System.Drawing.Point(604, 431)
$refreshButton.Size = New-Object System.Drawing.Size(108, 34)
$form.Controls.Add($refreshButton)

$prepareButton = New-Object System.Windows.Forms.Button
$prepareButton.Text = "Prepare Drive"
$prepareButton.Location = New-Object System.Drawing.Point(720, 431)
$prepareButton.Size = New-Object System.Drawing.Size(108, 34)
$prepareButton.Enabled = $false
$form.Controls.Add($prepareButton)

$closeButton = New-Object System.Windows.Forms.Button
$closeButton.Text = "Close"
$closeButton.Location = New-Object System.Drawing.Point(720, 476)
$closeButton.Size = New-Object System.Drawing.Size(108, 34)
$form.Controls.Add($closeButton)

$currentDrives = @()

$populateDrives = {
    $driveList.Items.Clear()
    $detailsLabel.Text = "Select a drive to see details."
    $statusLabel.Text = ""
    $prepareButton.Enabled = $false

    $script:currentDrives = @(Get-CandidateDrives)

    foreach ($drive in $script:currentDrives) {
        $item = New-Object System.Windows.Forms.ListViewItem($drive.DeviceID)
        [void]$item.SubItems.Add($drive.VolumeName)
        [void]$item.SubItems.Add($drive.DriveTypeLabel)
        [void]$item.SubItems.Add($drive.FileSystem)
        [void]$item.SubItems.Add((Get-ReadableSize $drive.Size))
        [void]$item.SubItems.Add((Get-ReadableSize $drive.FreeSpace))
        $item.Tag = $drive
        [void]$driveList.Items.Add($item)
    }

    if ($driveList.Items.Count -eq 0) {
        $detailsLabel.Text = "No eligible external drives were found. Plug in a drive and click Refresh Drives."
    }
}

$driveList.Add_SelectedIndexChanged({
    if ($driveList.SelectedItems.Count -eq 0) {
        $detailsLabel.Text = "Select a drive to see details."
        $prepareButton.Enabled = $false
        return
    }

    $selectedDrive = $driveList.SelectedItems[0].Tag
    $detailsLabel.Text = "Selected: $($selectedDrive.DeviceID)  |  $($selectedDrive.VolumeName)  |  $($selectedDrive.DriveTypeLabel)  |  $($selectedDrive.FileSystem)  |  $((Get-ReadableSize $selectedDrive.Size)) total, $((Get-ReadableSize $selectedDrive.FreeSpace)) free"
    $prepareButton.Enabled = $true
})

$refreshButton.Add_Click($populateDrives)

$prepareButton.Add_Click({
    if ($driveList.SelectedItems.Count -eq 0) {
        return
    }

    $selectedDrive = $driveList.SelectedItems[0].Tag
    if (-not (Show-ConfirmDialog -DriveLetter $selectedDrive.DeviceID -VolumeName $selectedDrive.VolumeName)) {
        return
    }

    $form.UseWaitCursor = $true
    $statusLabel.Text = "Preparing $($selectedDrive.DeviceID)..."
    $prepareButton.Enabled = $false
    $refreshButton.Enabled = $false

    try {
        $result = Invoke-DrivePreparation -DriveLetter $selectedDrive.DeviceID
        if ($result.ExitCode -eq 0) {
            $statusLabel.Text = "Drive prepared successfully."
            [System.Windows.Forms.MessageBox]::Show(
                "Drive $($selectedDrive.DeviceID) is ready.`n`nThe Allocator and the User Backups folder were created successfully.",
                "Drive Prepared",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Information
            ) | Out-Null
        }
        else {
            $statusLabel.Text = "Drive preparation failed."
            $message = if ([string]::IsNullOrWhiteSpace($result.Output)) {
                "Drive preparation failed with exit code $($result.ExitCode)."
            } else {
                $result.Output
            }

            [System.Windows.Forms.MessageBox]::Show(
                $message,
                "Drive Preparation Failed",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Error
            ) | Out-Null
        }
    }
    finally {
        $form.UseWaitCursor = $false
        $prepareButton.Enabled = $driveList.SelectedItems.Count -gt 0
        $refreshButton.Enabled = $true
        & $populateDrives
    }
})

$closeButton.Add_Click({ $form.Close() })

& $populateDrives
[void]$form.ShowDialog()
