# Publisher RIP

Publisher RIP is a small Windows app for taking a bunch of images, PDFs, or Outlook attachments and printing them full-page in order.

## Install

**[Download the installer](https://github.com/KiwiGeek/PublisherRIP/releases/latest/download/PublisherRip-win-Setup.exe)** and run it. Or install from PowerShell:

```powershell
irm https://raw.githubusercontent.com/KiwiGeek/PublisherRIP/master/scripts/install.ps1 | iex
```

The script downloads the same installer, checks its signature, and runs it.

Or with [winget](https://learn.microsoft.com/windows/package-manager/winget/):

```powershell
winget install KiwiGeek.PublisherRIP
```

The installer is signed, installs just for you (no admin rights needed), and starts the app when it's done. Windows SmartScreen may show a warning for a new release until it has been downloaded enough times; choose **More info → Run anyway** if you see it.

### Shortcuts and command-line launcher

The first time Publisher RIP starts, it asks which of these you'd like:

- Desktop shortcut
- Start Menu shortcut
- `publisherrip` command-line launcher (added to your PATH; open a new terminal to use it)

You can change these any time from the settings button (⚙) in the top-right corner of the window.

For unattended installs, set any of these environment variables to `yes` or `no` before running the installer, and the app will apply them without asking:

```powershell
$env:PUBLISHERRIP_INSTALL_DESKTOP_SHORTCUT = "no"
$env:PUBLISHERRIP_INSTALL_STARTMENU_SHORTCUT = "yes"
$env:PUBLISHERRIP_INSTALL_CLI_ALIAS = "yes"
```

### Updates

Publisher RIP checks for updates when it starts and downloads them in the background. When one is ready, either choose **Restart to update** from the settings menu, or just keep working and it will be installed the next time you open the app. You can also choose **Check for updates** from the same menu.

### Uninstall

Use **Settings → Apps → Installed apps → Publisher RIP → Uninstall**. This also removes the shortcuts and the `publisherrip` launcher.

The app is installed to `%LOCALAPPDATA%\PublisherRip`.

## Requirements

- Windows 10 (version 2004) or Windows 11, x64

## Notes

- The app stays in a small always-on-top window for quick drag-and-drop use.
- Paper size, orientation, printer selection, and similar settings come from the normal Windows print dialog.
- Images and PDF pages are scaled to fit the printer's printable area while preserving aspect ratio.
- Select a queued page and use Rotate Left or Rotate Right to turn it 90 degrees. Rotation applies to the preview and printed page without changing the original file.

## Development

Requires the .NET 10 SDK.

```powershell
dotnet run --project PublisherRip.App
```

Install, update, and setup features are disabled when running a development build; they only work in an installed copy.

### Releasing

Push a version tag:

```powershell
git tag v1.2.3
git push origin v1.2.3
```

The [Release workflow](.github/workflows/release.yml) builds a self-contained app, packages it with [Velopack](https://velopack.io), signs it with Azure Artifact Signing, verifies the signatures, and publishes a GitHub release. Tags with a suffix (for example `v1.2.3-beta.1`) are published as pre-releases, and only installs that are already on a pre-release receive them.

To test packaging or updates locally, install the `vpk` tool and pack into a folder, then set `PUBLISHERRIP_UPDATE_SOURCE` to that folder before launching an installed copy to make it update from there instead of GitHub.
