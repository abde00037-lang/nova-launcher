# Changelog

All notable changes to **Nova Launcher** (*Nova Workbench UI*) are documented in this file.

## [v2.1.0] — 2026-11-24
### Added
- **Standalone Full Offline Installer (`Nova-Setup-Full.exe`):** Single-file Windows setup embedding Nova Workbench, `nova-cli`, and the full `.NET 8` + `WPF` runtime for zero-dependency offline installation.
- **Lightweight Web Installer (`Nova-Web-Setup.exe`):** 66 KB online setup with resilient native `curl.exe` TLS downloading that always fetches the newest GitHub Release.
- **Live Top-Bar Version Indicator:** Displays the active build version directly in the Workbench title strip.
- **Automated `SHA256SUMS.txt` & CI Verification:** GitHub Actions workflows for pull-request verification (`ci.yml`) and release checksum generation (`release.yml`).

## [v2.0.0] — 2026-11-24
### Added
- **1-Click OTA Self-Updater (`LauncherUpdater`):** Automatically checks GitHub Releases on startup and swaps `bin\` in ~3 seconds while preserving 100% of `instances\`, `saves\`, `mods\`, `accounts\`, and `settings.json`.
- **Native Fabric & NeoForge Support:** Full 1-click installation alongside Forge and Vanilla (`1.8` – `1.21.4+`), both in the Workbench UI and `nova-cli` (`fabric install`, `neoforge install`).
- **Bulk SHA-1 Mod Updater (`ModUpdater`):** Scans 130+ `.jar` and `.disabled` files in a single Modrinth `/v2/version_files/update` request with atomic `.updating` swaps.
- **Drag-and-Drop Mod Installation & 1-Click Crash Suspect Disabler:** Drop `.jar` files directly onto `Installed Mods`, or disable crash-causing mods straight from the alert banner.
- **Nova Workbench UI Redesign:** Clean dark theme with customizable hardware accent swatches, live diagnostic stream, and dual Modrinth/CurseForge catalog.

### Fixed
- **Modern Forge / NeoForge (`1.17+` `BootstrapLauncher`):** Fixed embedded `${library_directory}`, `${classpath_separator}`, `${primary_jar}` placeholder substitution and duplicate `-cp` injection in `LaunchBuilder`.
- **Modpack Importer (`PackImporter`):** Added parallel CurseForge `manifest.json` mod downloading, automatic modloader installation, Java runtime provisioning, and CurseForge required dependency resolution (`relationType == 3`).
