# Changelog

All notable changes to **Nova Launcher** (*Nova Workbench UI*) are documented in this file.

## [Unreleased]

## [v2.1.13] — 2026-11-24
- Fix white backgrounds in AI provider and protocol dropdowns; give popup items and the provider list colors consistent with Nova's dark theme.
- Add quick-setup presets for AgentRouter and OneAIKey using their documented OpenAI-compatible and Anthropic-compatible endpoints and model IDs.
- Keep provider configurations editable and API keys protected locally with Windows DPAPI.

## [v2.1.12] — 2026-11-24
- Add the BYOK AI assistant with OpenAI-compatible and Anthropic protocols, model fallback, encrypted key storage, an in-app AI Chat page, and `nova-cli ai` management commands.
- Add a supervised agent for both protocols. Read-only diagnostics can run automatically; every write tool requires explicit approval. Mod updates and installs create verified mod snapshots.
- Add mod snapshot management in the Mods page: show stored size and missing-file state; create, restore, or permanently delete a selected archived snapshot after confirmation. Restore preflights all files, verifies SHA-256, saves a recovery snapshot, and disables later-added mods instead of deleting them.
- Add a dedicated AI approval card and expanded regression coverage for agent tools, snapshot integrity/recovery/deletion, and GUI workflows.
- Relicense the project from MIT to CC BY-NC 4.0 for releases except v2.0.0, which remains MIT; add third-party notices for the bundled .NET runtime, preserve those notices through offline installation, and describe external/runtime-downloaded components.

## [v2.1.11]
- Fix a v2.1.10 regression where inner scrolling could shrink the whole page to the minimum workspace size, leaving unused space on larger windows.
- Keep the mod-page header and filter controls from overlapping at narrower widths.
- Add WPF layout regressions for all main pages, nested scrolling, resizing, and mod-control overlap.

## [v2.1.10]
- Add a fullscreen button and F11 shortcut (F11 or Escape exits fullscreen); restore the previous window state afterward.
- Correct maximized window bounds for the current monitor's work area, keeping the Windows taskbar usable.
- Lower the minimum window size, make the navigation rail scrollable, and provide a scrollable workspace when the available area is too small for a page.
- Designed for Windows per-monitor DPI; the small-window fallback uses scrollbars rather than claiming every layout reflows at every resolution.

## [v2.1.9]
- Reduce launcher work when switching large modpacks: scan installed mod metadata only when the Mods page is open, and cancel outdated background scans when changing profiles or pages.
- Load world names and snapshots off the UI thread; avoid walking every world file during normal selection. **CALCULATE SIZES** computes world disk usage on request.
- Virtualize the installed-mods and worlds lists so only visible rows are rendered. Mod covers load on the Mods page and pause while a game runs.
- Preserve world management, snapshots, mod controls and the normal Minecraft launch process. Performance depends on hardware and profile contents.

## [v2.1.8]
- Open, rename, move to Nova trash, or directly join a world from the selected instance's Worlds page.
- Rename changes only the Minecraft display name in `level.dat`; a verified copy of the previous metadata is kept separately. Removal moves the entire world into the instance's `.nova-world-trash` instead of permanently deleting it.
- Direct join requires version metadata advertising singleplayer Quick Play (normally Java 1.20+). Older versions can still open the world from Minecraft's menu.
- Close Minecraft before modifying worlds, and keep independent backups. File locks and filesystem checks reduce risk but cannot prevent every race with external processes.

## [v2.1.7]
- Pin individual mod filenames per instance and preview Modrinth changelogs before updates.
- Snapshot top-level mod files before bulk updates, verify individual backups, and restore an updated mod only if its recorded replacement is unchanged.
- Prevent conflicting mod updates and accidental filename overwrites; report partial failures accurately.
- Protect CurseForge API keys with Windows DPAPI and migrate legacy plaintext on successful save.

## [v2.1.6] — 2026-11-25
- Resume interrupted update downloads and verify the complete package and runtime.
- Back up and verify replaced files; report installation failures and attempt rollback.
- Show update progress and the final installer result.

## [v2.1.5] — 2026-11-24
- Improved modpack download progress, retries, alternate sources, and missing-file reports.
- Report Minecraft client and Forge installer failures rather than marking them successful.

## [v2.1.4] — 2026-11-24
- Avoided quadratic lookup cost while skipping large Native JAR metadata lists.

## [v2.1.3] — 2026-11-24
- Hardened archive extraction, modpack imports, resource limits, and download concurrency; see the source tests for coverage.
- Split game-log formatting and updater responsibilities incrementally without changing the view-model bindings.
- Centralized the project version in `Directory.Build.props`.

## [v2.1.2] — 2026-11-24
- Validated offline names and instance IDs, protected Microsoft tokens with Windows DPAPI, and enabled SHA-256 verification for subsequent updater downloads.
- Removed the fixed Minecraft version-list ceiling and improved download diagnostics. See `docs/SECURITY-UPDATE-2.1.2.md` for limitations.

## [v2.1.1] — 2026-11-24
### Fixed
- The OTA payload now contains a complete self-contained .NET/WPF publish instead of framework-dependent configs. This avoids the v2.1.0 runtime mismatch at the cost of a larger download.
- CLI self-updates target the running launcher bin directory, even when its data root differs.
- CI tests overlaying the new package on the v2.1.0 portable bundle with .NET runtime lookup isolated, and checks that sample data is preserved.

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
