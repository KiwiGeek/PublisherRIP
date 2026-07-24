[CmdletBinding()]
param(
    [string]$Branch,
    [string]$SourcePath,
    [string]$InstallPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepositoryZipUrlTemplate = "https://github.com/KiwiGeek/PublisherRIP/archive/refs/heads/{0}.zip"

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Test-TruthyValue {
    param([AllowNull()][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    switch ($Value.Trim().ToLowerInvariant()) {
        "1" { return $true }
        "true" { return $true }
        "yes" { return $true }
        "y" { return $true }
        "on" { return $true }
        default { return $false }
    }
}

function Read-YesNoPrompt {
    param(
        [string]$Prompt,
        [bool]$DefaultValue = $false
    )

    while ($true) {
        $suffix = if ($DefaultValue) { "[Y/n]" } else { "[y/N]" }
        $response = Read-Host "$Prompt $suffix"

        if ([string]::IsNullOrWhiteSpace($response)) {
            return $DefaultValue
        }

        switch ($response.Trim().ToLowerInvariant()) {
            "y" { return $true }
            "yes" { return $true }
            "n" { return $false }
            "no" { return $false }
        }

        Write-Host "Please answer y or n." -ForegroundColor Yellow
    }
}

function Invoke-Native {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        $joined = if ($Arguments.Count -gt 0) { $Arguments -join " " } else { "" }
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $joined"
    }
}

function Resolve-DefaultSourcePath {
    return Join-Path ([System.IO.Path]::GetTempPath()) "PublisherRip-Source"
}

function Get-DownloadedSourcePath {
    param([string]$RepoBranch)

    $stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "PublisherRip-Source"
    $zipPath = Join-Path ([System.IO.Path]::GetTempPath()) "PublisherRip-Source.zip"

    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }

    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

    $zipUrl = [string]::Format($RepositoryZipUrlTemplate, $RepoBranch)
    Write-Step "Downloading source archive for branch '$RepoBranch'"
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath

    Write-Step "Expanding source archive"
    Expand-Archive -LiteralPath $zipPath -DestinationPath $stagingRoot -Force

    $sourceRoot = Get-ChildItem -LiteralPath $stagingRoot -Directory | Select-Object -First 1
    if ($null -eq $sourceRoot) {
        throw "The downloaded source archive did not contain an extracted project folder."
    }

    return $sourceRoot.FullName
}

function Publish-App {
    param(
        [string]$RepoPath,
        [string]$TargetInstallPath
    )

    $projectPath = Join-Path $RepoPath "PublisherRip.App\PublisherRip.App.csproj"
    if (-not (Test-Path -LiteralPath $projectPath)) {
        throw "Could not find project file at '$projectPath'."
    }

    $publishPath = Join-Path ([System.IO.Path]::GetTempPath()) "PublisherRip-Publish"

    if (Test-Path -LiteralPath $publishPath) {
        Remove-Item -LiteralPath $publishPath -Recurse -Force
    }

    Write-Step "Publishing app"
    Invoke-Native -FilePath "dotnet" -Arguments @(
        "publish",
        $projectPath,
        "-c",
        "Release",
        "--output",
        $publishPath,
        "--nologo"
    )

    if (-not (Test-Path -LiteralPath $publishPath)) {
        throw "Publish output folder was not created at '$publishPath'."
    }

    if (Test-Path -LiteralPath $TargetInstallPath) {
        Write-Step "Clearing previous install at $TargetInstallPath"
        Get-ChildItem -LiteralPath $TargetInstallPath -Force | Remove-Item -Recurse -Force
    }
    else {
        New-Item -ItemType Directory -Path $TargetInstallPath -Force | Out-Null
    }

    Write-Step "Copying published files to $TargetInstallPath"
    Copy-Item -Path (Join-Path $publishPath "*") -Destination $TargetInstallPath -Recurse -Force
}

function Get-ExePath {
    param([string]$TargetInstallPath)

    $exePath = Join-Path $TargetInstallPath "PublisherRip.App.exe"
    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "Published executable not found at '$exePath'."
    }

    return $exePath
}

function New-ShortcutFile {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory,
        [string]$Description
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = "$TargetPath,0"
    $shortcut.Description = $Description
    $shortcut.Save()
}

function Install-DesktopShortcut {
    param([string]$TargetPath)

    $desktopPath = [Environment]::GetFolderPath("Desktop")
    $shortcutPath = Join-Path $desktopPath "Publisher RIP.lnk"
    Write-Step "Creating desktop shortcut"
    New-ShortcutFile `
        -ShortcutPath $shortcutPath `
        -TargetPath $TargetPath `
        -WorkingDirectory (Split-Path -Parent $TargetPath) `
        -Description "Launch Publisher RIP"
}

function Install-StartMenuShortcut {
    param([string]$TargetPath)

    $programsPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
    $shortcutPath = Join-Path $programsPath "Publisher RIP.lnk"
    Write-Step "Creating Start Menu shortcut"
    New-ShortcutFile `
        -ShortcutPath $shortcutPath `
        -TargetPath $TargetPath `
        -WorkingDirectory (Split-Path -Parent $TargetPath) `
        -Description "Launch Publisher RIP"
}

function Ensure-PathEntry {
    param([string]$PathEntry)

    $currentUserPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $entries = @()
    if (-not [string]::IsNullOrWhiteSpace($currentUserPath)) {
        $entries = $currentUserPath.Split(';', [StringSplitOptions]::RemoveEmptyEntries)
    }

    if ($entries -notcontains $PathEntry) {
        $newPath = if ($entries.Count -eq 0) { $PathEntry } else { ($entries + $PathEntry) -join ';' }
        [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
        $env:Path = $newPath + ";" + [Environment]::GetEnvironmentVariable("Path", "Machine")
    }
}

function Install-CliAlias {
    param([string]$TargetInstallPath)

    $binPath = Join-Path $TargetInstallPath "bin"
    New-Item -ItemType Directory -Path $binPath -Force | Out-Null

    $shimPath = Join-Path $binPath "publisherrip.cmd"
    $shimContent = @"
@echo off
start "" "%~dp0..\PublisherRip.App.exe"
"@

    Set-Content -Path $shimPath -Value $shimContent -Encoding ASCII
    Ensure-PathEntry -PathEntry $binPath
    Write-Step "Installed CLI command: publisherrip"
}

if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet is required. Install the .NET 10 SDK and make sure 'dotnet' is on PATH."
}

if (-not $PSBoundParameters.ContainsKey("Branch")) {
    $Branch = if ([string]::IsNullOrWhiteSpace($env:PUBLISHERRIP_BRANCH)) { "master" } else { $env:PUBLISHERRIP_BRANCH }
}

if (-not $PSBoundParameters.ContainsKey("SourcePath")) {
    $SourcePath = if ([string]::IsNullOrWhiteSpace($env:PUBLISHERRIP_SOURCE_PATH)) { "" } else { $env:PUBLISHERRIP_SOURCE_PATH }
}

if (-not $PSBoundParameters.ContainsKey("InstallPath")) {
    $InstallPath = if ([string]::IsNullOrWhiteSpace($env:PUBLISHERRIP_INSTALL_PATH)) {
        Join-Path $env:LOCALAPPDATA "PublisherRip"
    }
    else {
        $env:PUBLISHERRIP_INSTALL_PATH
    }
}

$InstallPath = [System.IO.Path]::GetFullPath($InstallPath)

Write-Step "Using install path: $InstallPath"

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Get-DownloadedSourcePath -RepoBranch $Branch
}
else {
    $SourcePath = [System.IO.Path]::GetFullPath($SourcePath)
    Write-Step "Using local source path: $SourcePath"
}

Publish-App -RepoPath $SourcePath -TargetInstallPath $InstallPath

$exePath = Get-ExePath -TargetInstallPath $InstallPath

$InstallDesktopShortcut = if ([string]::IsNullOrWhiteSpace($env:PUBLISHERRIP_INSTALL_DESKTOP_SHORTCUT)) {
    Read-YesNoPrompt -Prompt "Create a desktop shortcut?" -DefaultValue $false
}
else {
    Test-TruthyValue -Value $env:PUBLISHERRIP_INSTALL_DESKTOP_SHORTCUT
}

$InstallStartMenuShortcut = if ([string]::IsNullOrWhiteSpace($env:PUBLISHERRIP_INSTALL_STARTMENU_SHORTCUT)) {
    Read-YesNoPrompt -Prompt "Create a Start Menu shortcut?" -DefaultValue $false
}
else {
    Test-TruthyValue -Value $env:PUBLISHERRIP_INSTALL_STARTMENU_SHORTCUT
}

$InstallCliAlias = if ([string]::IsNullOrWhiteSpace($env:PUBLISHERRIP_INSTALL_CLI_ALIAS)) {
    Read-YesNoPrompt -Prompt "Install the 'publisherrip' command-line launcher?" -DefaultValue $false
}
else {
    Test-TruthyValue -Value $env:PUBLISHERRIP_INSTALL_CLI_ALIAS
}

if ($InstallDesktopShortcut) {
    Install-DesktopShortcut -TargetPath $exePath
}

if ($InstallStartMenuShortcut) {
    Install-StartMenuShortcut -TargetPath $exePath
}

if ($InstallCliAlias) {
    Install-CliAlias -TargetInstallPath $InstallPath
}

Write-Host ""
Write-Host "Publisher RIP is ready." -ForegroundColor Green
Write-Host "Installed to: $InstallPath"
Write-Host "Executable:  $exePath"
