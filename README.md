<div align="center">

<img src="docs/logo.png" alt="Nova Launcher Logo" width="104" />

# Nova Launcher

**Fast, Portable Minecraft Launcher & Safe Modpack Manager for Windows**  
*Featuring the **Nova Workbench** desktop UI & `nova-cli` automation engine*

[**English**](README.md) · [**العربية**](README.ar.md) · [**Changelog**](CHANGELOG.md) · [**Security**](SECURITY.md) · [**Contributing**](CONTRIBUTING.md)

<br />

[![Latest Release](https://img.shields.io/github/v/release/abde00037-lang/nova-launcher?style=for-the-badge&label=RELEASE&color=e07a5f&logo=github)](https://github.com/abde00037-lang/nova-launcher/releases/latest)
[![CI Status](https://img.shields.io/github/actions/workflow/status/abde00037-lang/nova-launcher/ci.yml?style=for-the-badge&label=CI%20BUILD&logo=githubactions&logoColor=white)](https://github.com/abde00037-lang/nova-launcher/actions)
[![Platform](https://img.shields.io/badge/WINDOWS-10%20%2F%2011%20(64--BIT)-1f2937?style=for-the-badge&logo=windows)](https://github.com/abde00037-lang/nova-launcher/releases/latest)
[![Runtime](https://img.shields.io/badge/.NET-8.0%20EMBEDDED-512bd4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/LICENSE-MIT-0ea5e9?style=for-the-badge)](LICENSE)

<br />

[**⬇ Download Full Offline Setup**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Setup-Full.exe) · [**⚡ Web Setup**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Web-Setup.exe) · [**📦 Portable `.zip`**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Portable-Full-win-x64.zip) · [**🔐 SHA-256 Checksums**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/SHA256SUMS.txt)

</div>

---

## Overview

**Nova Launcher** (powered by the **Nova Workbench** desktop UI) is a fast, self-contained Minecraft launcher, modloader installer, and modpack manager built on **.NET 8 & WPF**.

The portable package keeps the application and its data under a launcher directory rather than `%APPDATA%\.minecraft`. The full installer defaults to `%LOCALAPPDATA%\NovaWorkbench` and can create Windows shortcuts. Back up important data before updates.

<p align="center">
  <img src="docs/screenshots/showcase.gif" alt="Nova Launcher — Animated Tour" width="96%" />
</p>

---

## Latest update — v2.1.5

- Modpack download progress, alternate sources, retries, and missing-file reports.
- Clearer Minecraft client and Forge installer failures; earlier archive safety improvements remain.
- Back up data before updating; check incomplete imports before playing.

[Download v2.1.5](https://github.com/abde00037-lang/nova-launcher/releases/tag/v2.1.5) · [Full changelog](CHANGELOG.md)

---

## Why Nova Launcher?

*Comparison snapshot as of November 2026 — Prism Launcher and Modrinth App are both great launchers. Nova offers a portable bundle and an OTA updater.*

| Capability | Nova Launcher | Prism Launcher | Modrinth App |
| :--- | :---: | :---: | :---: |
| **Portable by Default** (zero `%APPDATA%` footprint out of the box) | **Yes** | Optional portable zip | Installed app |
| **OTA updates** (overlays app files in `bin\`) | **Yes** | Full app update | Built-in updater |
| **Account Modes Supported** | **Microsoft + Standalone Offline** | Microsoft (+ offline after MS login) | Microsoft |
| **Native 1-Click Modloaders** (`Vanilla`, `Forge`, `Fabric`, `NeoForge`) | **Yes** | Yes (+ Quilt) | Yes (+ Quilt) |
| **Built-in Mod Sources** (with automatic required dependencies) | **Modrinth + CurseForge** | Modrinth + CurseForge | Modrinth |
| **Crash Suspect Pinpointing + 1-Click `DISABLE MOD` Action** on alert banner | **Yes** | Crash log analyzer & upload | Crash log viewer |
| **Full Headless CLI (`nova-cli`)** for scripting installs, packs, repairs & updates | **Yes** | Launch/import CLI flags | GUI-focused |

---

## Download & Installation

> **Updating from v2.1.0:** v2.1.1 fixes an OTA runtime-configuration issue. If an older installation cannot start after an update, back up your data before recovering it with the full installer. Starting in v2.1.1, OTA packages include the full runtime and are larger than v2.1.0’s.

Full packages include .NET 8; Java and Minecraft files are downloaded when needed. Pick the package that fits your workflow. See the latest Releases page for exact sizes and checksums:

| Package | Size | Best For | Direct Link |
| :--- | :---: | :--- | :---: |
| **`Nova-Setup-Full.exe`** *(Recommended)* | See release | **Complete Offline Installer.** Embeds Nova Launcher, `nova-cli`, and the full `.NET 8` runtime in one `.exe`. Installs in seconds without internet and creates Desktop & Start Menu shortcuts. | [**Download Full `.exe`**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Setup-Full.exe) |
| **`Nova-Web-Setup.exe`** | See release | **Tiny Online Installer.** Ideal for quick sharing on Discord/Telegram; automatically fetches and installs the newest GitHub release. | [**Download Web `.exe`**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Web-Setup.exe) |
| **`Nova-Portable-Full-win-x64.zip`** | See release | **Zero-Setup Portable Archive.** Extract anywhere (PC drive or USB stick) and run `Start Nova.bat` or `bin\Nova.exe`. | [**Download `.zip`**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Portable-Full-win-x64.zip) |
| **`nova-bin-win-x64.zip`** | See release | **OTA Update Payload.** Downloaded automatically by Nova's built-in **UPDATE & RESTART** banner. | *Handled Automatically* |

### System Requirements
- **OS:** Windows 10 (version 1809+) or Windows 11 — **64-bit (`x64`)**
- **RAM:** 4 GB minimum (8 GB+ recommended for heavy Forge/NeoForge modpacks)
- **Disk:** ~180 MB extracted for Nova Launcher + space for Minecraft assets, portable JDKs, and instances
- **Dependencies:** **None** — `.NET 8` is bundled inside Full packages, and Adoptium Java is provisioned when needed for supported Minecraft versions.

---

## Accounts, Security & SHA-256 Verification

### How Accounts & Login Work
Nova supports **both** official and offline play seamlessly from the **Accounts** tab:
1. **Official Microsoft Accounts (Device-Code OAuth2):** Sign in securely via Microsoft's official browser device-code flow (`microsoft.com/link`). Nova never sees your password — tokens are stored locally in `accounts/`, protected with Windows DPAPI for the current user, and refreshed automatically. Existing plaintext backups are not encrypted automatically.
2. **Standalone Offline Profiles:** Create instant local player handles with deterministic offline UUIDs for singleplayer, LAN worlds, and offline-mode servers — no prior online login or internet connection required.

### Windows SmartScreen & Antivirus Transparency
Because Nova Launcher is a free, open-source project without a paid `$400+/yr` commercial EV code-signing certificate:
- Windows Defender SmartScreen may show **"Windows protected your PC"** on first run — click **More info → Run anyway**.
- Heuristic scanners occasionally flag tiny web downloaders (`Nova-Web-Setup.exe`) because they fetch `.zip` archives over HTTPS. If that happens, use **`Nova-Setup-Full.exe`** or **`Nova-Portable-Full-win-x64.zip`**, which contain 100% of files offline.
- For v2.1.1, the four packages and checksums were built and uploaded by [GitHub Actions](https://github.com/abde00037-lang/nova-launcher/actions/runs/45464347081). For v2.1.0, the four binary assets were built and uploaded by [run #3](https://github.com/abde00037-lang/nova-launcher/actions/runs/45438579706); `SHA256SUMS.txt` was added separately afterwards. The [current release workflow](.github/workflows/release.yml) is configured to build these packages and generate the checksum file for future releases. GitHub also displays per-asset hashes.

### Release Checksums (SHA-256)

Use `Get-FileHash <file> -Algorithm SHA256` and compare it with the `SHA256SUMS.txt` attached to the **same release** as the downloaded file. Find assets and their sizes on the [latest Releases page](https://github.com/abde00037-lang/nova-launcher/releases/latest). Checksums provide integrity, not an independent signature.

---

## Core Features

### 1. Overview Dashboard & Live Telemetry
Real-time diagnostic stream, active JVM & heap telemetry, 1-click **Doctor Check / Repair Files**, and automatic pre-launch world snapshots.

<p align="center">
  <img src="docs/screenshots/overview.png" alt="Nova Launcher — Overview Dashboard" width="96%" />
</p>

### 2. Multi-Modloader Matrix (`Vanilla` · `Forge` · `Fabric` · `NeoForge`)
Install any official release (`1.8` – `1.21.4+`) cleanly or pair it in one click with **Forge**, **Fabric**, or **NeoForge**, with automatic Java `8` / `17` / `21` provisioning.

<p align="center">
  <img src="docs/screenshots/versions.png" alt="Nova Launcher — Versions & Modloaders" width="96%" />
</p>

### 3. Dual Mod Catalog, Drag-and-Drop & Bulk SHA-1 Updater
- **Modrinth + CurseForge Catalog:** Browse and install mods filtered automatically to your instance's Minecraft version and modloader, with required dependencies resolved automatically.
- **Bulk SHA-1 Mod Updater:** Hashes 130+ `.jar` (and `.disabled`) files in seconds against Modrinth `/v2/version_files/update` and swaps updates atomically without losing `.disabled` states.
- **Crash Suspect Disabler:** Automatically parses crash reports and adds a **1-click `DISABLE <MOD>`** button right on the crash alert banner.

<p align="center">
  <img src="docs/screenshots/store.png" alt="Nova Launcher — Mod Catalog & Store" width="96%" />
</p>

---

## Portable Directory Architecture

```text
Nova/                       # Portable archive root (illustrative)
├── Start Nova.bat          # Quick launcher shortcut
├── settings.json           # Accent theme, CurseForge API key, OTA update channel
├── accounts/               # Saved Microsoft OAuth2 & Offline player profiles
├── bin/                    # Launcher, CLI and bundled runtime; OTA overlays files here
│   ├── Nova.exe            # Nova Workbench desktop UI
│   ├── nova-cli.exe        # Headless command-line interface
│   ├── Nova.Core.dll       # Shared core engine
│   ├── hostfxr.dll, hostpolicy.dll, coreclr.dll  # Bundled .NET runtime components
│   └── ...                 # Additional .NET/WPF and app files (not shown)
├── instances/              # Isolated modpacks & profiles (mods, config, saves)
├── versions/               # Mojang, Forge, Fabric & NeoForge manifests
├── libraries/              # Shared Maven library cache (deduplicated across packs)
├── assets/                 # Shared Minecraft asset object store
├── java/                   # Auto-provisioned portable Adoptium Java
├── backups/                # Non-destructive world snapshots (.zip)
└── logs/                   # UI & diagnostic logs
```

---

## Command-Line Interface (`nova-cli`)

The CLI defaults to `bin\data` when launched from `bin\`, unlike the desktop UI which uses the parent launcher folder. Pass `--root <launcher-directory>` to use the same data as the UI.

```powershell
# List instances & detected Java runtimes
nova-cli instances
nova-cli java

# Install modloaders directly
nova-cli forge install 1.20.1 recommended
nova-cli fabric install 1.21.4
nova-cli neoforge install 1.21.1

# Create, verify, repair, and launch an instance
nova-cli new 1.20.1 --forge recommended --name "Create Pack" --memory 6144 --repair
nova-cli doctor create-pack --deep
nova-cli launch create-pack

# Bulk-check & update installed mods via SHA-1
nova-cli mods create-pack --check-updates
nova-cli mods create-pack --update

# Import / export Modrinth (.mrpack) & CurseForge (.zip) modpacks
nova-cli pack import MyPack.mrpack --name "My Modpack"
nova-cli pack export create-pack --worlds

# Check or apply launcher OTA updates
nova-cli self-update check --root "C:\path\to\Nova"
nova-cli self-update apply --root "C:\path\to\Nova"
```

---

## Frequently Asked Questions (FAQ)

**1. Will updating Nova Launcher delete my worlds, mods, or accounts?**  
The updater copies the staged application files into `bin\`; it does not intentionally delete your instances, worlds, mods or accounts. Back up important data before updating. This is a file overlay, not a binary delta patch.

**2. Do I need to install Java or .NET 8 manually?**  
**For the initial full installation, no separate .NET installation is needed.** The installer contains a compressed portable bundle; on extraction, `bin\` contains the app alongside hundreds of .NET 8/WPF runtime files, including `coreclr.dll`, `hostfxr.dll` and `hostpolicy.dll`. The v2.1.0 OTA archive contained framework-dependent files and could break a self-contained installation. Starting with v2.1.1, OTA packages include the self-contained application and runtime; they are larger, but do not overwrite self-contained configuration with framework-dependent files. Java is downloaded when required into `java\` for supported versions.

**3. How do I enable CurseForge mod search alongside Modrinth?**  
Modrinth works out of the box with zero configuration. To unlock CurseForge search and modpack downloads, paste a free Eternal API key from [console.curseforge.com](https://console.curseforge.com/) into **Configuration → CurseForge API Token**.

**4. Can I move my entire Nova folder to another PC or USB drive?**  
You can copy the launcher and world data, but paths in instance settings may need adjustment. Microsoft tokens are bound to the Windows user and PC by DPAPI; sign in again after moving if required.

---

## Building, Security & Contributing

- **Architecture & Local Build:** See [**CONTRIBUTING.md**](CONTRIBUTING.md)
- **Vulnerability Reporting:** See [**SECURITY.md**](SECURITY.md)
- **Release History:** See [**CHANGELOG.md**](CHANGELOG.md)

## License

Released under the [MIT License](LICENSE).  
*Nova Launcher is an independent open-source project and is not affiliated with or endorsed by Mojang Studios or Microsoft.*
