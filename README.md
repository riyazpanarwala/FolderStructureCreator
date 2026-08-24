# Folder Structure Creator (WPF)

A feature-rich Windows desktop application built with .NET 8 WPF. Easily browse your Windows folder directory, visually design nested folder blueprints (manually or imported from reference folders), and build/sync them to disk in one click — with support for both an interactive indented **Tree View** and a visual dendrogram **Org Chart Diagram**.

---

## 🌟 Key Features

### 🛠️ Building & Designing the Blueprint Plan
- **+ Add Root Folder** — Create top-level root folders to start building a structure plan from scratch.
- **Quick Batch-Add** — Type comma-separated folder names (e.g., `src, docs, tests, scripts`), press **Enter** (or click **Add**), and all of them are added at once. If a folder is selected, they are added as subfolders; otherwise, as new root folders.
- **📥 Import Reference Folder** — Pick any folder on your computer and import its entire folder hierarchy (names + nesting) directly into your blueprint plan.
- **Hang-Proof Import** — Bounded directory enumeration ensures safety with huge folders (capped per level and total depth) to prevent app freezing.
- **Clear Plan** — Clear all folders from the screen draft at once with one click (never affects files on disk).

### 🖱️ Right-Click Folder Context Menu
Right-clicking any folder node (in either Tree View or Org Chart View) provides full folder management:
- **➕ Add Child** — Add a subfolder inside the selected folder.
- **➕ Add Sibling** — Add a new folder at the same hierarchy level alongside the selected folder.
- **✏️ Rename** — Inline text editing to rename the selected folder.
- **🗑️ Delete** — Delete the selected folder and all its contents (removes from blueprint, or sends to Windows Recycle Bin in Live Sync mode).
- **📂 Open in Explorer** — Instantly open the folder's physical location on your computer in Windows Explorer.
- **🔍 Focus Folder (Fit Selection)** — *(Org Chart view)* Centers and zooms the view onto the selected folder box.

### 🔄 Live Computer Sync Mode
- **Real-Time Sync Checkbox** — Toggle "Live computer sync" mode on the top toolbar.
- When enabled, any addition, inline rename, node deletion (sent safely to Windows Recycle Bin), or drag-and-drop move immediately updates the actual physical folders on your hard drive in real time.

### 👁️ Two Blueprint Views
- **Tree View** — Classic, clean indented list view with expandable/collapsible tree nodes.
- **Org Chart Diagram** — A horizontal dendrogram diagram with depth-colored boxes and right-angle connector lines.
  - **Zooming & Panning** — Smooth zoom (Ctrl + Mouse Wheel or toolbar buttons from 10% to 400%, Reset zoom, and Fit-to-View) and canvas panning (Middle-click drag or Right-click canvas drag).
  - **Drag & Drop Moving** — Drag any box and drop it onto another node to instantly move/re-parent it in the tree structure.
  - **Double-Click to Rename** — Double-click any box on the diagram canvas to rename it inline.

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
src/FolderStructureCreator/
  App.xaml / App.xaml.cs          Application startup & global exception handling
  MainWindow.xaml / .xaml.cs      Main WPF UI layout, search popup & commands binding
  AppIcon.ico                     App icon (executable icon & title bar icon)
  Models/
    FolderNode.cs                 Blueprint folder model (Tree & Org Chart data structure)
    FileSystemNode.cs             Real computer directory node (lazy-loaded browser)
    PinnedFolder.cs               Pinned quick-access folder model
  Views/
    OrgChartView.xaml(.cs)        Custom canvas-drawn interactive org-chart diagram (dendrogram renderer)
  Services/
    FileSystemService.cs          Bounded directory scanning, sanitization, disk creation & Recycle Bin operations
    NaturalStringComparer.cs      Natural string comparer for numerical (1..10) and alphabetical (A-Z) sorting
    PinnedFoldersService.cs       Service to persist and manage pinned target locations
  ViewModels/
    MainViewModel.cs              Core MVVM ViewModel (commands, search, live sync, selection, navigation)
    RelayCommand.cs / ViewModelBase.cs  Base MVVM primitives
  Converters/                     XAML visibility, brush, and grid length converters
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

## 📦 Deployment & Installer

### 1. Single-File Executable (Self-Contained)
Run the following command to produce a portable single `.exe` file that works without pre-installed .NET runtimes:
```powershell
dotnet publish src/FolderStructureCreator/FolderStructureCreator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```
The resulting executable is located at `publish/FolderStructureCreator.exe`.

### 2. Team Installer (Inno Setup)
An Inno Setup script is included to generate a standard Windows installer setup (`FolderStructureCreatorSetup.exe`):
```powershell
cd installer
.\build-installer.ps1
```
Output location: `installer/installer-output/FolderStructureCreatorSetup.exe`.
