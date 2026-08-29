# Folder Structure Creator (WPF)

A feature-rich Windows desktop application built with .NET 8 WPF. Easily browse your Windows folder directory, visually design nested folder blueprints (manually or imported from reference folders), and build/sync them to disk in one click — with support for both an interactive indented **Tree View** and a visual dendrogram **Org Chart Diagram**.

---

## 🌟 Key Features

### 🛠️ Building & Designing the Blueprint Plan
- **+ Add Root Folder** — Create top-level root folders to start building a structure plan from scratch.
- **Quick Batch-Add** — Type comma-separated folder names (e.g., `src, docs, tests, scripts`), press **Enter** (or click **Add**), and all of them are added at once. If a folder is selected, they are added as subfolders; otherwise, as new root folders.
- **📥 Import Reference Folder & Drag & Drop** — Pick any folder on your computer OR drag and drop folders directly from Windows Explorer into the application window/TreeView to instantly import its directory hierarchy.
- **🚫 Smart Import & Ignore Rules (`.structureignore`)** — Automatically filters out build, cache, and system subfolders (`node_modules`, `.git`, `.vs`, `bin`, `obj`, `dist`, `build`, etc.) during imports, and respects `.structureignore` or `.gitignore` files found in the source directory. Can be toggled on/off via the toolbar.
- **Hang-Proof Import** — Bounded directory enumeration ensures safety with huge folders (capped per level and total depth) to prevent app freezing.
- **Clear Plan** — Clear all folders from the screen draft at once with one click (never affects files on disk).

### 🖱️ Right-Click Folder Context Menu & Drag-and-Drop
Right-clicking any folder node (in either Tree View or Org Chart View) provides full folder management:
- **➕ Add Child** — Add a subfolder inside the selected folder.
- **➕ Add Sibling** — Add a new folder at the same hierarchy level alongside the selected folder.
- **✏️ Rename** — Inline text editing to rename the selected folder.
- **🗑️ Delete** — Delete the selected folder and all its contents (removes from blueprint, or sends to Windows Recycle Bin in Live Sync mode).
- **⬆️ Move to Root** — Move any nested subfolder out to become a top-level root folder.
- **🔀 Drag & Drop Re-parenting** — Drag any folder node in Tree View or Org Chart View and drop it onto another node to re-parent it, or drop on empty space to move to root.
- **📂 Open in Explorer** — Instantly open the folder's physical location on your computer in Windows Explorer.
- **🔍 Focus Folder (Fit Selection)** — *(Org Chart view)* Centers and zooms the view onto the selected folder box.

### 📜 Export Standalone Executable Scripts
Export your blueprint folder plan directly as a standalone executable script that can create the folder structure on any computer without needing the app installed:
- **PowerShell (`.ps1`)** — Standalone PowerShell script with interactive target path parameters and colorized output.
- **Windows Batch (`.bat`)** — Fast batch script for classic Windows command line deployment.
- **Linux / macOS Bash (`.sh`)** — Cross-platform shell script formatted with LF Unix line endings for Linux/macOS environments.

### 🔄 Live Computer Sync Mode
- **Real-Time Sync Checkbox** — Toggle "Live computer sync" mode on the top toolbar.
- When enabled, any addition, inline rename, node deletion (sent safely to Windows Recycle Bin), or drag-and-drop move immediately updates the actual physical folders on your hard drive in real time.

### 👁️ Two Blueprint Views
- **Tree View** — Classic, clean indented list view with expandable/collapsible tree nodes.
- **Org Chart Diagram** — Interactive dendrogram diagram with depth-colored boxes and right-angle connector lines.
  - **📷 Diagram Export (PNG / SVG / PDF)** — Export the complete dendrogram diagram to high-resolution PNG images, SVG vector graphics, or PDF vector documents with full depth coloring and connector lines.
  - **Layout Direction Toggle** — Dynamically switch diagram orientation between **Horizontal (Left-to-Right ➡️)** and **Vertical (Top-to-Bottom ⬇️)** dendrogram views via the toolbar toggle button.
  - **🗺️ Floating Mini-Map Navigation Overlay** — Interactive bird's-eye thumbnail overlay providing total navigation control across large diagrams:
    - **Draggable Window Header** — Reposition the mini-map window anywhere on the canvas by dragging its top header bar.
    - **Interactive Viewfinder & Smooth Pan** — Live viewfinder rect (`#38BDF8`) showing current viewport bounds; click or drag to pan with smooth animated scrolling.
    - **Viewfinder Resizing & Mouse Wheel Zoom** — Drag viewfinder borders/corners or scroll mouse wheel on the mini-map to zoom in/out dynamically.
    - **Selection & Search Match Overlays** — Visual indicator dots highlight the selected node (`🎯`) and search result matches (`🔍`) with hover tooltips.
    - **Expand / Shrink & Minimize Badge** — Toggle between standard (160×110) and expanded (260×175) window sizes (`↗`/`↙`), minimize into a floating corner badge (`🗺️ Mini-Map`), or double-click to Fit-to-View (`🎯`). Toggle visibility via `🗺️ Map` on the main toolbar.
  - **Zooming & Panning** — Smooth zoom (Ctrl + Mouse Wheel or toolbar buttons from 10% to 400%, Reset zoom, and Fit-to-View) and canvas panning (Middle-click drag or Right-click canvas drag).
  - **Double-Click to Open in Explorer** — Double-click any folder node in Tree View or box on the diagram canvas to open its physical location in Windows Explorer.

### ⚡ Command Line (CLI) & Direct Folder-to-Folder Copy
- **Direct Folder Replication** — Replicate any existing directory's folder hierarchy directly to a target destination from terminal/PowerShell without launching the GUI.
- **Dry-Run Preview** — Preview all nested folder paths that would be created before writing anything to disk.
- **CLI Options & Syntax**:
  ```cmd
  FolderStructureCreator.exe --source <source_folder> --target <target_folder> [options]
  ```
  | Flag | Option | Description |
  | :--- | :--- | :--- |
  | `-src`, `--source` | `<path>` | Source reference folder to copy structure from. |
  | `-dst`, `--target` | `<path>` | Destination target folder where structure will be created. |
  | `--dry-run` | None | Preview folder creation simulation without disk writes. |
  | `--no-ignore` | None | Disable automatic `.structureignore` / `.gitignore` and default ignore rules. |
  | `--silent`, `-s` | None | Run headlessly without opening the GUI window. |
  | `-h`, `--help` | None | Display CLI help documentation. |

### 🔍 Advanced Search & Highlight
- **Real-Time Search Bar** — Search across all folders in your blueprint plan (`Ctrl + F` shortcut).
- **Match Dropdown & Highlight** — Displays matching folder paths in a popup dropdown list.
- **Visual Highlighting** — Highlights matching folder nodes in yellow across both Tree View and Org Chart View.
- **Navigation Controls** — Jump through matches using **Enter** (Next) / **Shift+Enter** (Previous) or the match navigation arrow buttons.

### 📁 Live Computer Directory Browser & Targets
- **Lazy-Loaded Windows Drives Browser** — Browse drives, pinned folders, and real computer directories on the left sidebar. Nothing is loaded until expanded, ensuring maximum performance even on huge drives.
- **Natural Folder Sorting & Sort Order Toggle** — Folder names are sorted naturally by number first (`1, 2, 3 ... 10, 11`), followed by alphabetical names (`A-Z`). Includes a **Sort: A-Z ⬇ / Sort: Z-A ⬆** toolbar toggle to dynamically switch between ascending and descending sort order across pinned folders and drive trees.
- **Pinned Folders** — Pin frequently used folder locations for quick target selection.
- **One-Click Structure Creation** — Recursively creates every folder in your plan under the target directory path. Existing folders are preserved safely.
- **Auto-Expansion** — After creation, the live browser automatically expands down to display all newly created folders.
- **Windows Path Sanitization** — Automatically sanitizes invalid filename characters (`<>:"/\|?*`) and reserved names (`CON`, `PRN`, `AUX`, etc.).

---

## 💻 Requirements
- Windows 10 or Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) for building)

---

## 🏗️ Project Structure
```
FolderStructureCreator.sln
release.bat                       1-Click batch script to tag & push a new release
release.ps1                       1-Click PowerShell script to tag & push a new release
.github/workflows/
  release.yml                     GitHub Actions CI/CD workflow for automated releases
src/FolderStructureCreator/
  App.xaml / App.xaml.cs          Application startup & global exception handling
  MainWindow.xaml / .xaml.cs      Main WPF UI layout, search popup, drag-and-drop & commands
  AppIcon.ico                     App icon (executable icon & title bar icon)
  Models/
    FolderNode.cs                 Blueprint folder model (Tree & Org Chart data structure)
    FileSystemNode.cs             Real computer directory node (lazy-loaded browser)
    PinnedFolder.cs               Pinned quick-access folder model
  Views/
    OrgChartView.xaml(.cs)        Custom canvas-drawn interactive org-chart diagram (dendrogram renderer)
  Services/
    FileSystemService.cs          Bounded directory scanning, sanitization, disk creation & Recycle Bin operations
    IgnoreRuleService.cs          Smart ignore filter engine (.structureignore / .gitignore support)
    NaturalStringComparer.cs      Natural string comparer for numerical (1..10) and alphabetical (A-Z) sorting
    PinnedFoldersService.cs       Service to persist and manage pinned target locations
    ScriptGeneratorService.cs     Generates standalone PowerShell (.ps1), Batch (.bat), and Bash (.sh) creation scripts
  ViewModels/
    MainViewModel.cs              Core MVVM ViewModel (commands, search, live sync, selection, navigation)
    RelayCommand.cs / ViewModelBase.cs  Base MVVM primitives
  Converters/                     XAML visibility, brush, layout direction, and grid length converters
installer/
  FolderStructureCreator.iss      Inno Setup installer script
  build-installer.ps1             PowerShell build & package script
```

---

## 🔨 Build & Run

### Using Visual Studio 2022
1. Open `FolderStructureCreator.sln`.
2. Press **F5** to build and launch the application.

### Using .NET CLI
```powershell
dotnet build src/FolderStructureCreator/FolderStructureCreator.csproj
dotnet run --project src/FolderStructureCreator/FolderStructureCreator.csproj
```

---

## 📦 Deployment & Release Options

### 1. ⚡ Lightweight Portable Executable (~0.3 MB / 320 KB)
Produces a super-fast, tiny executable for machines that have [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) installed:
```powershell
dotnet publish src/FolderStructureCreator/FolderStructureCreator.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```
Output location: `publish/FolderStructureCreator.exe` (~320 KB).

### 2. 🚀 Self-Contained Compressed Standalone Executable (~68 MB)
Generates a standalone `.exe` that runs on any Windows PC without requiring .NET pre-installed (with single-file assembly compression enabled):
```powershell
dotnet publish src/FolderStructureCreator/FolderStructureCreator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```
Output location: `publish/FolderStructureCreator.exe` (~68 MB).

### 3. 📦 Team Installer (Inno Setup)
Generates a standard Windows installer setup (`FolderStructureCreatorSetup.exe`) with Desktop & Start Menu shortcuts:
```powershell
cd installer
.\build-installer.ps1
```
Output location: `installer/installer-output/FolderStructureCreatorSetup.exe`.

### 🤖 Automated Releases via GitHub Actions
This project includes an automated GitHub Actions workflow ([.github/workflows/release.yml](file:///.github/workflows/release.yml)).

#### ⚡ Release Options:
Whenever you want to trigger a new release, use any of these options:

- **Option A (Double-Click Batch File):** Double-click [release.bat](file:///release.bat) in Windows Explorer and type the version (e.g. `5.0.2`).
- **Option B (Git Terminal Alias):** Run `git tag-push v5.0.2` in your terminal.
- **Option C (PowerShell Script):** Run `.\release.ps1 v5.0.2` in PowerShell.
- **Option D (Manual Git Commands):** Run `git tag v5.0.2` followed by `git push origin v5.0.2`.

All options will create and push the version tag to GitHub, triggering GitHub Actions to build and publish the release.

#### Generated Release Assets:
- **`FolderStructureCreator.exe` (~0.3 MB)** — Ultra-lightweight portable executable (Framework-Dependent).
- **`FolderStructureCreator_Portable_Lightweight.zip` (~0.3 MB)** — Lightweight portable executable zipped.
- **`FolderStructureCreator_Standalone.exe` (~68 MB)** — Self-contained compressed portable executable (no .NET required).
- **`FolderStructureCreatorSetup.exe` (~47 MB)** — Full Windows Setup Wizard installer.
