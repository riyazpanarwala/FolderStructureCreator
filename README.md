# Folder Structure Creator (WPF)

A Windows desktop app: browse your real folder tree, visually design a nested folder
blueprint (by hand or imported from an existing folder), and create the whole thing in
one click — with a choice of an editable list view or a visual org-chart diagram.

## Features

**Building the plan**
- **Manual builder** — Add Root Folder, Add Child (nests one level in), Add Sibling (same level), double-click to rename, Delete (with confirmation). The edit toolbar only appears once you've selected a folder, so the screen isn't cluttered with buttons that don't apply yet.
- **Quick Add** — type `src, docs, tests, assets`, hit Enter (or the button, which only appears once you've typed something), and all of them are added as children of the selected folder in one go.
- **Import Existing Folder** — pick any real folder on disk and its entire subfolder structure (names + nesting) is copied straight into the plan. Folders only — files in that reference folder are never read or copied.
- **Import is hang-proof** — reading a reference folder is hard-capped: max 500 subfolders per directory level, ~8000 folders total, 60 levels deep. If a folder's too big to fully read, the import stops early and tells you rather than freezing the app.
- **Clear Plan** — wipe the current plan and start over (only affects what's on screen, never touches disk).

**Two ways to see the plan**
- **Tree View** — the classic indented, editable list.
- **Org Chart View** — a horizontal box-and-line diagram, boxes colored by depth level and connected with elbow lines (dendrogram-style), similar to a mind-map/org-chart tool. Click a box to select it (drives the same edit toolbar as Tree View), double-click to rename in place. Automatically takes the full window width (the directory browser panel collapses out of the way) since the diagram needs the room. Switching to Org Chart happens automatically right after an import, since that's the best way to take in a large structure at a glance; the two view buttons show which one's active with a filled highlight.

**Picking a target & creating**
- **Live directory browser** — a real, lazy-loaded view of your Windows folders (nothing is scanned until you expand it, so it stays fast even on huge drives). Click any folder to set it as the target — clicking also expands that folder, so you can drill down one click at a time.
- **One-click create** — recursively creates every folder in the plan under the target path. Existing folders are left alone, so it's always safe to re-run. After creating, the live browser auto-expands down through everything that was just made, so you see the full result immediately.
- Invalid Windows filename characters are stripped automatically; reserved names (`CON`, `PRN`, etc.) are handled; folder creation continues past a failed branch instead of aborting the whole run.

**Polish**
- Custom app icon (teal folder mark matching the app's own color palette) — set as the .exe icon, the window's title-bar icon, and the installer wizard icon.
- Tooltips on every toolbar button explain what it does, instead of a wall of instructional text.
- Packaged with an Inno Setup installer for handing out to a team (see below).

## Requirements
- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Desktop workload if using Visual Studio)
- Visual Studio 2022 (17.8+) with the ".NET desktop development" workload, **or** just the `dotnet` CLI

## Project structure
```
FolderStructureCreator.sln
src/FolderStructureCreator/
  App.xaml / App.xaml.cs          Startup, global exception handling
  MainWindow.xaml / .xaml.cs      Full UI layout + tree-selection/rename/org-chart wiring
  AppIcon.ico                     App icon (exe icon + window title-bar icon)
  Models/
    FolderNode.cs                 Blueprint node (structure builder / org chart), incl. IsFile flag
    FileSystemNode.cs             Real folder node (lazy-loaded browser)
  Views/
    OrgChartView.xaml(.cs)        Custom canvas-drawn org-chart diagram (alternate view of the plan)
  Services/
    FileSystemService.cs          Safe/bounded enumeration, sanitization, import, recursive creation
  ViewModels/
    MainViewModel.cs              All commands and state (MVVM)
    RelayCommand.cs / ViewModelBase.cs
  Converters/                     Visibility/brush/grid-length converters for XAML bindings
installer/
  FolderStructureCreator.iss      Inno Setup script
  build-installer.ps1             One-command publish + compile
  AppIcon.ico                     Installer wizard icon
```

## Build & run

### Visual Studio
1. Open `FolderStructureCreator.sln`.
2. Set `FolderStructureCreator` as the startup project (it's the only one).
3. Press **F5**.

### CLI
```powershell
cd FolderStructureCreator
dotnet build
dotnet run --project src/FolderStructureCreator/FolderStructureCreator.csproj
```

## Testing plan
Since this is a UI-driven desktop tool, prioritize these manual + a few automated checks:

**Automated (recommended addition):** extract `FileSystemService` logic is already pure/static and easy to unit test with xUnit + a temp directory fixture:
- `SanitizeFolderName` — invalid chars, reserved names, empty input, trailing dots/spaces.
- `CreateStructure` — nested tree creates correct paths; re-running is idempotent (no exceptions, `AlreadyExistedCount` increments); a locked/invalid path produces an `Errors` entry without throwing.

```powershell
dotnet new xunit -o tests/FolderStructureCreator.Tests
dotnet add tests/FolderStructureCreator.Tests reference src/FolderStructureCreator/FolderStructureCreator.csproj
```

**Manual smoke test checklist:**
1. Launch app — left tree shows real drives; expanding a drive loads real subfolders. Window and taskbar both show the app icon.
2. Click a folder on the left — Target Path box updates and that folder expands.
3. With nothing selected in the plan, confirm only Root Folder / Import / Clear Plan are visible — Child/Sibling/Rename/Delete should be hidden, not just grayed out.
4. Build a structure: root `Project` → children `src`, `docs` → grandchild under `src`: `components`. Selecting a folder should reveal the Child/Sibling/Rename/Delete row.
5. Switch to **Org Chart** — the left directory panel should collapse away and the diagram should take the full window width, boxes colored by depth, connected with elbow lines. Click a box to select it, double-click to rename. Switch back to **Tree View** — left panel returns, both toggle buttons show the correct one highlighted.
6. Click **Import Existing Folder**, pick a real folder with a few subfolders — the plan should populate and the view should auto-switch to Org Chart.
7. Click **Create Structure** — verify status message and that folders now exist on disk (left tree auto-refreshes and expands down through everything just created).
8. Re-click **Create Structure** with the same plan — should report "already existed", no errors, no duplicates.
9. Try a folder name with `<>:"/\|?*` — verify it's sanitized rather than crashing.
10. Point the target at a path with no write permission (e.g. `C:\Windows\System32`) — verify a clean error message, not a crash.
11. Quick-add `a, b, c` under a selected node — verify all three appear as siblings, and the "Add as children" button disappears once the box is cleared.
12. Delete a node with children — confirm dialog appears; cancel vs. confirm both behave correctly.
13. Resize the window / drag the splitter (Tree View mode) — layout should remain usable down to ~900px wide.

## Deployment (giving someone a runnable .exe)

**Self-contained single-file (no .NET install required on target machine):**
```powershell
dotnet publish src/FolderStructureCreator/FolderStructureCreator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```
This produces `publish/FolderStructureCreator.exe` — copy that one file anywhere on a Windows machine and run it.

**Framework-dependent (smaller, requires .NET 8 Desktop Runtime on target machine):**
```powershell
dotnet publish src/FolderStructureCreator/FolderStructureCreator.csproj -c Release -o publish
```

**Auto-run on schedule/login (optional, matches your existing Task Scheduler pattern):**
Since this app is interactive (not a background script), if you want it available at login rather than
scheduled execution, add a shortcut to `FolderStructureCreator.exe` in:
`shell:startup` (Win+R → `shell:startup`)

## Installer for sharing with your team (Inno Setup)

The `installer/` folder has everything needed to produce a single `FolderStructureCreatorSetup.exe`
your team can double-click to install (Start Menu shortcut, optional desktop icon, clean uninstall).

**One-time setup:** install [Inno Setup](https://jrsoftware.org/isdl.php) (free) on your build machine.

**Build the installer (does publish + compile in one step):**
```powershell
cd installer
.\build-installer.ps1
```
Output: `installer-output/FolderStructureCreatorSetup.exe` — this is the single file to hand to your team
(Slack it, put it on a shared drive, whatever). It installs per-user by default, so teammates don't need
admin rights.

**Manual alternative** (if you'd rather not run the PowerShell script):
1. `dotnet publish` with the self-contained command above.
2. Open `installer/FolderStructureCreator.iss` in the Inno Setup app.
3. Click **Compile**. Output goes to `installer-output/FolderStructureCreatorSetup.exe`.

**Updating the version for a new release:** bump `#define MyAppVersion "1.0.0"` at the top of
`FolderStructureCreator.iss` before rebuilding, so teammates' installers show the correct version and
Windows treats it as an upgrade rather than a fresh install.

**Notes:**
- The installer bundles the entire self-contained publish output, so end users do **not** need the .NET runtime installed.
- `AppId` in the `.iss` is a fixed GUID — don't change it between releases, or Inno Setup will treat upgrades as separate installs instead of replacing the old version.
- The app ships with its own icon (`AppIcon.ico`) — it's set as the .exe icon, the window title-bar icon, and the installer wizard icon, so it's consistent everywhere. To swap it for something else, replace `src/FolderStructureCreator/AppIcon.ico` (used by the app) and `installer/AppIcon.ico` (used by the installer wizard) with your own `.ico` file of the same name.

## "Launch strategy" (for a one-person internal tool)
- v1: build for yourself, self-contained publish, keep the .exe in a personal tools folder.
- If sharing with others: zip the self-contained publish output, or set up a GitHub Release with the win-x64 zip attached; tag releases (`v1.0.0`) so you can roll back.
- If it grows scope (e.g. save/load templates, JSON import/export of structures): that's a natural v2 — the `FolderNode` model already has everything needed to serialize with `System.Text.Json`.

## Scalability / UX notes already baked in
- Left tree never scans the whole disk — only expanded nodes load, so it stays responsive even on drives with huge folder counts.
- Reading a reference folder during import is hard-capped (items/level, total folders, depth) so it can never hang on an oversized or pathologically nested folder.
- System/hidden folders are filtered out of the browser to reduce noise.
- Folder creation continues past a failed branch (e.g. permission-denied on one subfolder) instead of aborting the whole operation.
- All folder-name mutations funnel through `SanitizeFolderName`, so what you see in the plan is guaranteed creatable on Windows.
- The toolbar only shows buttons that apply to the current state (nothing selected → no edit buttons; empty quick-add box → no Add button), so the UI stays uncluttered without losing functionality.
