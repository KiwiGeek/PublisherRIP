# Publisher RIP

Publisher RIP is a small Windows app for taking a bunch of images, PDFs, or Outlook attachments and printing them full-page in order.

## Install Or Update

Run this in PowerShell:

```powershell
irm https://raw.githubusercontent.com/KiwiGeek/PublisherRIP/master/scripts/install.ps1 | iex
```

The installer will:

- download the latest GitHub ZIP for the selected branch
- build and install the app into `%LOCALAPPDATA%\PublisherRip`
- ask whether you want a Desktop shortcut
- ask whether you want a Start Menu shortcut
- ask whether you want a `publisherrip` command-line launcher

## Requirements

- Windows
- `.NET 10 SDK`

## After Install

Launch the app from whichever option you chose during install:

- Desktop shortcut
- Start Menu shortcut
- `publisherrip` in a new terminal

If you skipped all of those, you can still launch it directly from:

```text
%LOCALAPPDATA%\PublisherRip\PublisherRip.App.exe
```

## Notes

- The app stays in a small always-on-top window for quick drag-and-drop use.
- Paper size, orientation, printer selection, and similar settings come from the normal Windows print dialog.
- Images and PDF pages are scaled to fit the printer's printable area while preserving aspect ratio.
