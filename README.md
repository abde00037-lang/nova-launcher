# Nova Workbench — Portable Minecraft Launcher

**Nova Workbench** is a fast, portable Minecraft launcher & modpack manager for Windows built on **.NET 8 & WPF**, engineered with a clean separation between launcher binaries (`bin\`) and player data (`instances\`, `versions\`, `libraries\`, `assets\`, `saves\`).

## Highlights

- **4 Native Loaders (1-Click Install):** Vanilla (`1.8` – `1.21.4+`), **Forge**, **Fabric**, and **NeoForge**, with automatic Java `8` / `17` / `21` provisioning.
- **Smart Mod Management & Bulk SHA-1 Updater:**
  - Drag & drop `.jar` files directly into `Mods`.
  - 1-click SHA-1 bulk update check & atomic update against Modrinth (preserving `.disabled` state).
  - Integrated Modrinth & CurseForge catalog with automatic required dependency resolution.
- **Modpack Import & Export:** Full `.mrpack` (Modrinth) and CurseForge `.zip` pack importer with parallel downloads, plus portable pack exporter.
- **Built-in Self-Updater (OTA):**
  - Checks GitHub Releases (`abde00037-lang/nova-launcher`) automatically on startup.
  - Updates `bin\` in 2 seconds and restarts **without ever touching** your instances, mods, worlds, or accounts.
- **Diagnostics & Safety:** Doctor file verifier/repairer, Crash Analyzer with 1-click suspect mod disabler, and automatic world snapshots.

---

## Download & Installation

1. Go to [**Releases**](https://github.com/abde00037-lang/nova-launcher/releases/latest):
   - **New users / Friends (`Nova-Portable-Full-win-x64.zip`):** Extract anywhere and run `Start Nova.bat` or `bin\Nova.exe` (includes .NET runtime — works out of the box!).
   - **Existing Nova installs / OTA (`nova-bin-win-x64.zip`):** Handled automatically by Nova's built-in **UPDATE & RESTART** button!

---

## Publishing a New Version (e.g. `v3.0.0`)

Whenever code is pushed with a new version tag (e.g. `git tag v3.0.0 && git push origin v3.0.0`) — or triggered from the **Actions → Release Nova Workbench** tab on GitHub — GitHub Actions automatically compiles and publishes `nova-bin-win-x64.zip` & `Nova-Portable-Full-win-x64.zip`. Every user running Nova receives the **UPDATE AVAILABLE** banner on next launch!
