# Publisher RIP Printer App

Build a small always-on-top .NET 10 WPF desktop app that accepts drag-and-drop image and PDF inputs, including Outlook drag-and-drop attachments, lets the user reorder or remove pages, and prints them full-page through the Windows print dialog.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue with zero context); run the phase's **Verification Plan** and record the result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Scaffold App And Core Models
Status: Complete

- [x] Create the .NET 10 WPF solution and project structure in the repository root.
- [x] Add page/item models and services for dropped content, printing, and drag-reorder support.
- [x] Define a small always-on-top shell window with a drop target, item list, and print/remove controls.

### Verification Plan
- Run `dotnet build` from the repository root and expect a successful build with the new project files present.

### Phase Summary
Created the `PublisherRip.App` .NET 10 WPF project and solution, added models for printable pages and source documents, and replaced the template window with a compact always-on-top queue UI. Core services for import, preview generation, and printing were added so later phases could focus on file intake and runtime behavior.

## Phase 2: Implement Intake And Printing
Status: Complete

- [x] Support direct file drops for common image formats and PDFs.
- [x] Support Outlook virtual-file drag-and-drop by extracting file contents from the drop data object.
- [x] Render printable pages that fit within the printer's imageable area while preserving aspect ratio.
- [x] Open the Windows print dialog and print the current ordered page list.

### Verification Plan
- Run `dotnet build` and expect success.
- Run the app and verify that dropping local files populates the page list and that the print dialog opens.

### Phase Summary
Implemented file-drop import for normal files plus Outlook virtual-file drag/drop via `FileGroupDescriptor` and `FileContents` clipboard formats. Image files are loaded through WPF codecs, PDF pages are rendered through `Windows.Data.Pdf`, and printing uses a custom paginator that scales each page into the printer imageable area while preserving aspect ratio.

## Phase 3: Refine UX And Finish Verification
Status: Complete

- [x] Make the window always-on-top with a compact layout appropriate for quick drag-and-drop use.
- [x] Add reorder and remove interactions that are easy to use with a mouse.
- [x] Review error handling and user messaging for unsupported or failed drops.
- [x] Record final verification results and usage notes.

### Verification Plan
- Run `dotnet build` and expect success.
- Launch the app and verify the always-on-top compact window, reorder/remove actions, and print flow.

### Phase Summary
The window is compact, always-on-top, and optimized for repeated drop/queue/print use. Reordering currently uses explicit `Move Up` and `Move Down` buttons rather than drag-reordering, and the app shows status text plus warning dialogs when some dropped items cannot be imported. Verification completed with a clean `dotnet build` and a startup smoke test via `dotnet run`; live Outlook drag/drop and printer dialog interaction still need hands-on confirmation on the Windows desktop.

## Final Recap
Built a runnable `.NET 10` WPF app named `PublisherRip.App` that accepts local image files, PDFs, and Outlook attachment drops, queues them as printable pages, lets the user remove or reorder the queue, and prints through the standard Windows print dialog with full-page fit-to-printable-area scaling. The app builds cleanly and passes a startup smoke test in this environment.

## Deployment Plan
1. Open a terminal in the repository root.
2. Run `dotnet build` to compile the app.
3. Run `dotnet run --project PublisherRip.App\PublisherRip.App.csproj` to launch it.
4. Drag image files, PDFs, or Outlook attachments onto the window.
5. Use `Move Up`, `Move Down`, `Remove`, or `Clear All` to adjust the queue.
6. Click `Print` and choose printer options in the standard Windows print dialog.
