[CmdletBinding()]
param(
    # Defaults to the latest stable release. Point at a specific release's Setup.exe to pin a version.
    [string]$SetupUrl = "https://github.com/KiwiGeek/PublisherRIP/releases/latest/download/PublisherRip-win-Setup.exe"
)

# Installs Publisher RIP by downloading the signed installer from GitHub Releases and running it.
#
#   irm https://raw.githubusercontent.com/KiwiGeek/PublisherRIP/master/scripts/install.ps1 | iex
#
# The app asks which shortcuts and launcher you want the first time it starts. For unattended
# installs, set any of these to yes/no first (they're passed through to the app):
#   PUBLISHERRIP_INSTALL_DESKTOP_SHORTCUT, PUBLISHERRIP_INSTALL_STARTMENU_SHORTCUT, PUBLISHERRIP_INSTALL_CLI_ALIAS
#
# Works in Windows PowerShell 5.1 and PowerShell 7+.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # the progress bar makes downloads very slow in Windows PowerShell

$ExpectedSignerPattern = "CN=Joshua Penman,*"

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$downloadDir = Join-Path ([System.IO.Path]::GetTempPath()) ("PublisherRip-Setup-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null
$setupPath = Join-Path $downloadDir "PublisherRip-win-Setup.exe"

try {
    Write-Step "Downloading the Publisher RIP installer"
    Invoke-WebRequest -Uri $SetupUrl -OutFile $setupPath -UseBasicParsing

    Write-Step "Checking the installer's signature"
    $signature = Get-AuthenticodeSignature -FilePath $setupPath
    if ($signature.Status -ne "Valid") {
        throw "The downloaded installer is not validly signed (status: $($signature.Status)). It was not run."
    }
    if ($signature.SignerCertificate.Subject -notlike $ExpectedSignerPattern) {
        throw "The downloaded installer is signed by an unexpected publisher ('$($signature.SignerCertificate.Subject)'). It was not run."
    }
    Write-Host "    Signed by: $($signature.SignerCertificate.GetNameInfo('SimpleName', $false))"

    Write-Step "Running the installer"
    $process = Start-Process -FilePath $setupPath -PassThru -Wait
    if ($process.ExitCode -ne 0) {
        throw "The installer exited with code $($process.ExitCode)."
    }

    Write-Host ""
    Write-Host "Publisher RIP is installed and will keep itself up to date." -ForegroundColor Green
    Write-Host "Uninstall it any time from Settings > Apps > Installed apps."
}
finally {
    Remove-Item -LiteralPath $downloadDir -Recurse -Force -ErrorAction SilentlyContinue
}
