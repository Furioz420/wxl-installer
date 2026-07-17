# WarcraftXL Installer

A modern, WPF-based installer/manager for [WarcraftXL](https://github.com/WarcraftXL) (WXL).
It walks you through installing WXL core, syncing community modules, deploying the
required DB2 files into your WoW 3.3.5a client, optionally cold-converting your custom
model assets, and building/deploying the final WXL host.

Built with .NET 8 and [WPF UI](https://github.com/lepoco/wpfui) (Fluent-style dark/gold
theme).

## Features

- **Wizard-style flow** with a persistent side navigation:
  1. Welcome & path configuration (WXL folder, WoW client folder)
  2. Cold Convert (optional) — pre-processes `.m2` / `.blp` assets
  3. Core & Modules — installs `wxl-core`, plus a live catalog of community
	 modules pulled from GitHub topics
  4. DB2 Files — pushes the required DB2 files into the next free
	 `Data\Patch-<letter>.mpq\DBFilesClient` folder
  5. Build & Deploy — runs `build.ps1` with clean/release/auto-patch options
- **Live module catalog** discovered from GitHub topics
  (`wxl-modules`, `wxl-scripts`, `warcraftxl-modules`, `warcraftxl-scripts`),
  plus explicitly curated repos such as:
  - `WarcraftXL/wxl-host-extension` (mandatory)
  - `Furioz420/wxl-retail-db2`
  - `Furioz420/wxl-equip-module-DB2` (auto-requires `wxl-retail-db2`)
  - `Furioz420/wxl-client-extensions`
- **Dependency-aware module selection** (e.g. equip-module-DB2 automatically
  requires retail-db2).
- **Equip-module DBC/DB2 preview** — when `DBCandDB2.zip` is present in the
  installed equip module, the DB2 page previews the exact files and the target
  patch, and the main "Install required DB2 files" button pushes them in one go.
- **DB2 file → patch preview** so you can see which files will be installed and
  where before you click Install.
- **Prompts to install pending module selections** when navigating away from the
  Core & Modules page.
- **Clean build auto-selected** by default on the Build & Deploy page.
- **On-disk GitHub API cache** to survive the 60-req/hour anonymous rate limit.
- Optional `GITHUB_TOKEN` / `WXL_GITHUB_TOKEN` env-var support (bumps rate limit
  to 5000/h and avoids first-run 403s in shared IP environments).

## Requirements

- Windows 10/11
- .NET 8 Desktop Runtime (or the .NET 8 SDK to build from source)
- A World of Warcraft 3.3.5a client (build 12340) with a valid `Wow.exe`

## Getting started (end user)

1. Download the latest release from the
   [Releases](https://github.com/Furioz420/wxl-installer/releases) page.
2. Extract and run `WXL Installer.exe`.
3. On **Welcome**, point:
   - **WXL folder** at where you want WXL core installed (or is already installed).
   - **WoW client folder** at the root of your 3.3.5a client (must contain `Wow.exe`).
4. Follow the wizard from top to bottom.

## Building from source

```powershell
git clone https://github.com/Furioz420/wxl-installer
cd wxl-installer
dotnet build "WXL Installer.slnx" -c Release
```

Or open `WXL Installer.slnx` in Visual Studio 2022+/2026 and press F5.

## Notes

- The installer never overwrites your original `Data\*.mpq` files. DB2 files are
  placed inside `Data\Patch-<letter>.mpq\DBFilesClient\` (next free letter, or
  the highest existing patch folder that already has a `DBFilesClient`).
- `wxl-host-extension` is always installed — it is required by WXL core.
- The GitHub catalog cache lives in `%LOCALAPPDATA%\WxlInstaller\gh-cache\`.
- Persistent installer settings live in `%APPDATA%\WxlInstaller\settings.json`.

## License

See the individual dependencies for their licenses. This installer itself is
provided as-is for the WarcraftXL community.
