# Folder Structure Creator (WPF)

A feature-rich Windows desktop application built with .NET 8 WPF. Easily browse your Windows folder directory, visually design nested folder blueprints (manually or imported from reference folders), and build/sync them to disk in one click — with support for an interactive indented **Tree View**, a visual dendrogram **Org Chart Diagram**, a **Spotlight Command Palette (`Ctrl+K`)**, a **Blueprint vs. Disk Visual Diff Engine**, and a complete **Dark / Light / High Contrast / System Sync Theme System**.

---

## 🌟 Key Features

### 🎨 Theme System (Dark, Light, High Contrast, & System Sync)
- **4 Theme Modes** — Switch seamlessly between 🌙 **Dark**, ☀️ **Light**, 🔲 **High Contrast**, and 💻 **System Match** (automatically detects Windows OS dark/light registry settings `AppsUseLightTheme` and `SystemParameters.HighContrast`).
- **Persistent Preferences** — Saves your theme choice automatically to `%APPDATA%/FolderStructureCreator/settings.json`.
- **Themed Scrollbars & Modern Tooltips** — Sleek rounded scrollbar thumbs and themed tooltips with subtle border glow, fully styled to match each theme palette.
- **Accessible Hover States** — High-contrast WCAG AAA compliant typography, box rendering, and hover feedback across all view toggles and action buttons.

### ⚡ Spotlight Command Palette (`Ctrl + K`)
- **Keyboard Spotlight Overlay** — Press `Ctrl + K` anywhere in the app (or click `⚡ Command Palette (Ctrl+K)` on the top toolbar) to open a spotlight command search bar over the app.
- **Fuzzy Search Across App Features** — Search and execute any command in 1 second:
  - *"Dark" / "Light"* ➔ Switch Application Themes
  - *"Diff"* ➔ Compare Blueprint against Physical Disk
  - *"Export"* ➔ Export Standalone Scripts or Diagram Images
  - *"Tree" / "Chart"* ➔ Toggle View Modes
  - *"Sync"* ➔ Toggle Live Computer Sync Mode
- **Keyboard Execution** — Use `Up` / `Down` arrow keys to navigate filtered actions and press `Enter` to run. Dismiss with `Esc`.

### 🔍 Blueprint vs. Disk Visual Diff & Sync
- **Target Folder Comparison** — Compare your designed blueprint plan against an existing physical target folder on disk in one click (**🔍 Diff vs Disk**).
- **Color-Coded Status Badges**:
  - 🟢 **`[+ MISSING]`** — Exists in your Blueprint, but **NOT** on physical disk (Emerald Green).
  - ⚪ **`[✓ MATCH]`** — Exists in **BOTH** your Blueprint and physical disk (Slate Neutral).
  - 🟠 **`[⚡ EXTRA]`** — Exists on **disk**, but is **NOT** in your Blueprint (Amber/Orange).
- **Incremental Creation** — Click **`🟢 Create Missing Only`** to create missing blueprint folders on disk without modifying or overwriting existing files.

### 🚦 Dynamic 3-Step Workflow Readiness Stepper
- **Live Readiness Indicators** — Clear header stepper indicators that guide the creation process:
  - **`1. Plan`** — Shows live count of planned folders (`✓ Plan (N)`), or indicates when a blueprint draft needs to be created.
  - **`2. Destination`** — Highlights destination selection (`✓ Destination`) once a target directory is chosen.
  - **`3. Create`** — Activates clearly (`▶ Ready to create`) when both requirements are met, transitions to `Creating...` during disk writes, and marks `✓ Created` once execution succeeds.
- **Header Status Feedback** — Clear inline status messages indicating real-time application state without blocking interaction.

### 🛠️ Building & Designing the Blueprint Plan
- **+ Add Root Folder** — Create top-level root folders to start building a structure plan from scratch.
- **Quick Batch-Add** — Type comma-separated folder names (e.g., `src, docs, tests, scripts`), press **Enter** (or click **Add**), and all of them are added at once. If a folder is selected, they are added as subfolders; otherwise, as new root folders.
- **📥 Import Reference Folder & Drag & Drop** — Pick any folder on your computer OR drag and drop folders directly from Windows Explorer into the application window/TreeView to instantly import its directory hierarchy.
- **🚫 Smart Ignore Rules (`.structureignore` / `.gitignore`)** — Automatically filters out build, cache, and system subfolders (`node_modules`, `.git`, `.vs`, `bin`, `obj`, `dist`, `build`, etc.) during imports, and respects `.structureignore` or `.gitignore` files found in the source directory. Can be toggled on/off in the **⚙ Settings** menu.
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
  - **Enhanced Selection & Framing** — Selected nodes feature a distinct turquoise glow ring and selection bullet (`● `) while preserving original depth colors. Viewport framing is clamped to natural 100% zoom max so small structures aren't unnaturally blown up.
  - **Non-Destructive Navigation** — Blueprint node additions and edits preserve your current scroll/pan position without jumpy resets.
  - **Expand/Collapse Controls** — Compact 22px interactive badge buttons displaying subfolder count with hover transitions.
  - **📷 Diagram Export (PNG / SVG / PDF)** — Export the complete dendrogram diagram to high-resolution PNG images, SVG vector graphics, or PDF vector documents with full depth coloring and connector lines.
  - **Layout Direction Toggle** — Dynamically switch diagram orientation between **Horizontal (Left-to-Right ➡️)** and **Vertical (Top-to-Bottom ⬇️)** dendrogram views via the toolbar toggle button.
  - **🔀 Connector Line Styles** — Switch between 3 diagram connection styles via toolbar button, context menu, or Command Palette:
    - **Orthogonal** (classic right-angles) — Clean 90° engineering layout.
    - **Curved** (smooth Bézier splines) — Modern flowing S-curves like Miro, Figma, and MindMeister.
    - **Straight** (direct diagonal tree lines) — Point-to-point diagonal lines for compact trees.
    *(Fully reflected in live canvas rendering, Fullscreen Meeting Mode, and PNG/SVG/PDF exports).*
  - **Zooming & Panning** — Smooth zoom (Ctrl + Mouse Wheel or toolbar buttons from 10% to 400%, Reset zoom, and Fit-to-View) and canvas panning (Middle-click drag or Right-click canvas drag).
  - **Double-Click to Open in Explorer** — Double-click any folder node in Tree View or box on the diagram canvas to open its physical location in Windows Explorer.

### 🖥️ Fullscreen Meeting Mode (`F11`)
- **Distraction-Free Architecture Whiteboard** — Press **`F11`** (or click **`Fullscreen`** on the chart/tree toolbar or in Settings) to hide all toolbars, destination sidebars, and edit controls, transforming the app into an edge-to-edge interactive architecture whiteboard.
- **Floating In-Meeting Controls** — Sleek top-right floating HUD providing one-click diagram layout toggling (Horizontal/Vertical), zoom controls (`-`, `+`, `Fit`), and a quick exit button (`✕ Exit Fullscreen`).
- **Quick Exit** — Press **`F11`** or **`Esc`** at any time to restore the standard window layout and toolbars.

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
- **Pinned Folders** — Pin frequently used folder locations for quick target selection. Features a 1-click **Chart** button to immediately visualize any pinned folder in the Org Chart diagram, and a **`...`** overflow menu to **Select as Destination**, **Open in Explorer**, or **Unpin Folder**.
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
  MainWindow.xaml / .xaml.cs      Main WPF UI layout, search popup, Command Palette, drag-and-drop & commands
  AppIcon.ico                     App icon (executable icon & title bar icon)
  Models/
    FolderNode.cs                 Blueprint folder model (Tree & Org Chart data structure)
    FileSystemNode.cs             Real computer directory node (lazy-loaded browser)
    PinnedFolder.cs               Pinned quick-access folder model
    CommandItem.cs                Spotlight Command Palette item model
    NodeDiffStatus.cs             Diff status enum (MissingOnDisk, MatchesDisk, ExtraOnDisk)
  Themes/
    DarkTheme.xaml                Dark teal-slate color palette dictionary
    LightTheme.xaml               Light crisp-white color palette dictionary
    HighContrastTheme.xaml        High Contrast accessibility color palette dictionary
  Views/
    OrgChartView.xaml(.cs)        Custom canvas-drawn interactive org-chart diagram (dendrogram renderer)
  Services/
    ThemeService.cs               Theme manager, OS registry theme detection & settings persistence
    DirectoryDiffService.cs       Recursive disk vs. blueprint comparison engine
    FileSystemService.cs          Bounded directory scanning, sanitization, disk creation & Recycle Bin operations
    IgnoreRuleService.cs          Smart ignore filter engine (.structureignore / .gitignore support)
    NaturalStringComparer.cs      Natural string comparer for numerical (1..10) and alphabetical (A-Z) sorting
    PinnedFoldersService.cs       Service to persist and manage pinned target locations
    ScriptGeneratorService.cs     Generates standalone PowerShell (.ps1), Batch (.bat), and Bash (.sh) creation scripts
  ViewModels/
    MainViewModel.cs              Core MVVM ViewModel (commands, themes, command palette, diff, search, live sync)
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

---

## 🤖 Automated Releases via GitHub Actions
This project includes an automated GitHub Actions workflow ([.github/workflows/release.yml](file:///.github/workflows/release.yml)) combined with an intelligent, one-click release script.

### ⚡ Interactive One-Click Release:
Whenever you want to publish a new release, simply:
- **Double-click** [release.bat](file:///release.bat) in Windows Explorer, OR
- **Run** `.\release.ps1` in PowerShell.

#### How It Works:
1. **Fetches from GitHub:** Automatically runs `git fetch --tags origin` to ensure it has all existing release tags.
2. **Detects Current Version:** Finds the highest existing version tag (e.g., `v5.0.4`).
3. **Presents an Interactive Bump Menu:**
   ```text
   ==================================================
    Latest release version detected: v5.0.4
   ==================================================

   Select the release bump type:
     [1] Patch : v5.0.5 (Bug fixes, minor tweaks) [Default]
     [2] Minor : v5.1.0 (New features, enhancements)
     [3] Major : v6.0.0 (Major overhaul or breaking change)
     [4] Custom: Enter a specific version manually
     [5] Cancel

   Choose an option [1-5] (Default is 1):
   ```
4. **One-Key Selection:**
   - Press **Enter** (or `1`) to immediately select the next **Patch** (`v5.0.5`).
   - Press `2` to select the next **Minor** (`v5.1.0`).
   - Press `3` to select the next **Major** (`v6.0.0`).
   - Press `4` to enter a custom version tag.
   - Press `5` or `q` to safely cancel.
5. **Auto Tag & Push:** Automatically creates the git tag and pushes it to GitHub, immediately triggering the GitHub Actions build & publish workflow!

*(Optional CLI Usage: You can also pass versions directly: `.\release.ps1 v5.0.5` or test with `.\release.ps1 -DryRun`.)*

### Generated Release Assets:
- **`FolderStructureCreator.exe` (~0.3 MB)** — Ultra-lightweight portable executable (Framework-Dependent).
- **`FolderStructureCreator_Portable_Lightweight.zip` (~0.3 MB)** — Lightweight portable executable zipped.
- **`FolderStructureCreator_Standalone.exe` (~68 MB)** — Self-contained compressed portable executable (no .NET required).
- **`FolderStructureCreatorSetup.exe` (~47 MB)** — Full Windows Setup Wizard installer.
