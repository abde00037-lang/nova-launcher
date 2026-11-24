<div align="center">

<img src="docs/logo.png" alt="Nova Launcher Logo" width="104" />

# Nova Launcher

**Fast, Portable Minecraft Launcher & Safe Modpack Manager for Windows**  
*Featuring the **Nova Workbench** desktop interface & `nova-cli` automation engine*

[**English**](README.md) · [**العربية**](README.ar.md) · [**Changelog**](CHANGELOG.md) · [**Contributing**](CONTRIBUTING.md)

<br />

[![Latest Release](https://img.shields.io/github/v/release/abde00037-lang/nova-launcher?style=for-the-badge&label=RELEASE&color=e07a5f&logo=github)](https://github.com/abde00037-lang/nova-launcher/releases/latest)
[![CI Status](https://img.shields.io/github/actions/workflow/status/abde00037-lang/nova-launcher/ci.yml?style=for-the-badge&label=CI%20BUILD&logo=githubactions&logoColor=white)](https://github.com/abde00037-lang/nova-launcher/actions)
[![Platform](https://img.shields.io/badge/WINDOWS-10%20%2F%2011%20(64--BIT)-1f2937?style=for-the-badge&logo=windows)](https://github.com/abde00037-lang/nova-launcher/releases/latest)
[![Runtime](https://img.shields.io/badge/.NET-8.0%20EMBEDDED-512bd4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/LICENSE-MIT-0ea5e9?style=for-the-badge)](LICENSE)

<br />

[**⬇ Download Full Offline Setup (`.exe`)**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Setup-Full.exe) · [**⚡ Web Setup (`66 KB`)**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Web-Setup.exe) · [**📦 Portable `.zip`**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Portable-Full-win-x64.zip) · [**🔐 SHA-256 Checksums**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/SHA256SUMS.txt)

</div>

---

## Overview

**Nova Launcher** (powered by the **Nova Workbench** desktop UI) is a fast, self-contained Minecraft launcher, modloader installer, and modpack manager built on **.NET 8 & WPF**.

Unlike traditional launchers that scatter files across `%APPDATA%\.minecraft` or require manual runtime setup, Nova keeps **100% of its files inside a single portable folder** — launcher executables live exclusively in `bin\`, while your instances, worlds, mods, libraries, Java runtimes, and accounts stay isolated and untouched across every update.

<p align="center">
  <img src="docs/screenshots/showcase.gif" alt="Nova Launcher — Animated Workbench Tour" width="96%" />
</p>

---

## Why Nova Launcher?

| Capability | Nova Launcher | Prism Launcher | Modrinth App |
| :--- | :---: | :---: | :---: |
| **Zero-Install Portable Folder** (USB-ready, nothing written to `%APPDATA%`) | **Yes** | Portable zip available | No (installed app) |
| **Safe 3-Second OTA Self-Updates** (`bin\` swap only — worlds/mods untouched) | **Yes** | Manual / installer | Built-in |
| **Microsoft OAuth2 + Offline Local Profiles** out of the box | **Both** | Requires MS account first | Microsoft only |
| **4 Native 1-Click Modloaders** (`Vanilla`, `Forge`, `Fabric`, `NeoForge`) | **Yes** | Yes | Yes |
| **Dual Mod Catalog** (Modrinth + CurseForge with auto-dependencies) | **Both** | Both | Modrinth only |
| **Crash Suspect Pinpointing + 1-Click Mod Disabler** on alert banner | **Yes** | Log viewer only | Log viewer only |
| **Full Headless CLI (`nova-cli`)** sharing the exact same core engine | **Yes** | Partial CLI flags | No |

---

## Download & Installation

No external `.NET` or Java installation is required — pick the package that fits your workflow:

| Package | Size | Best For | Direct Link |
| :--- | :---: | :--- | :---: |
| **`Nova-Setup-Full.exe`** *(Recommended)* | **~69.5 MB** | **Complete Offline Installer.** Embeds Nova Workbench, `nova-cli`, and the full `.NET 8` runtime in one `.exe`. Installs in seconds without internet and creates Desktop & Start Menu shortcuts. | [**Download Full `.exe`**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Setup-Full.exe) |
| **`Nova-Web-Setup.exe`** | **~66 KB** | **Tiny Online Installer.** Ideal for sharing on Discord/Telegram; automatically fetches and installs the newest GitHub release. | [**Download Web `.exe`**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Web-Setup.exe) |
| **`Nova-Portable-Full-win-x64.zip`** | **~69.5 MB** | **Zero-Setup Portable Archive.** Extract anywhere (PC drive or USB stick) and run `Start Nova.bat` or `bin\Nova.exe`. | [**Download `.zip`**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Portable-Full-win-x64.zip) |
| **`nova-bin-win-x64.zip`** | **~700 KB** | **OTA Update Payload.** Downloaded automatically by Nova's built-in **UPDATE & RESTART** banner. | *Handled Automatically* |

### System Requirements
- **OS:** Windows 10 (version 1809+) or Windows 11 — **64-bit (`x64`)**
- **RAM:** 4 GB minimum (8 GB+ recommended for heavy Forge/NeoForge modpacks)
- **Disk:** ~180 MB for Nova Launcher + space for Minecraft assets, portable JDKs, and instances
- **Dependencies:** **None** — `.NET 8` is bundled in Full packages, and Adoptium Java (`8` / `17` / `21`) is downloaded automatically per Minecraft version.

---

## Accounts, Security & SHA-256 Verification

### How Accounts & Login Work
Nova supports **both** official and offline play seamlessly from the **Accounts** tab:
1. **Official Microsoft Accounts (Device-Code OAuth2):** Sign in securely via Microsoft's official browser device-code flow (`microsoft.com/link`). Nova never sees your password — tokens are stored locally inside `accounts/` and refreshed automatically for online multiplayer servers.
2. **Offline / Local Profiles:** Create instant local player handles with deterministic offline UUIDs for singleplayer, LAN worlds, and offline-mode servers — zero internet connection required.

### Windows SmartScreen & Antivirus Transparency
Because Nova Launcher is a free, open-source community project without a paid `$400+/yr` commercial EV code-signing certificate:
- Windows Defender SmartScreen may show **"Windows protected your PC"** on first run — click **More info → Run anyway**.
- Some heuristic scanners flag lightweight web downloaders (`Nova-Web-Setup.exe`, 66 KB) simply because they download `.zip` files from GitHub. If that happens, use **`Nova-Setup-Full.exe`** or **`Nova-Portable-Full-win-x64.zip`**, which contain 100% of files offline.
- Every binary is compiled transparently from this repository via [**GitHub Actions**](.github/workflows/release.yml) and accompanied by **`SHA256SUMS.txt`**.

<details>
<summary><strong>🔐 Verify Release Checksums (<code>v2.1.0</code> SHA-256)</strong></summary>

Run `certutil -hashfile <file> SHA256` or `Get-FileHash <file> -Algorithm SHA256` in PowerShell and compare against [`SHA256SUMS.txt`](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/SHA256SUMS.txt):

```text
a70327929f0d397de66446aee350f630aa0da984a0bef93df26a3266d7fd8315  Nova-Setup-Full.exe
7d7aee48ec9fb1de576f4aacb13ea91e1c41fc1df8c03dccafe3ad396e42b156  Nova-Web-Setup.exe
af01fcdcf97a3aeb1ec15e1fb85dfcfb5bae8549feaf27acbacc07ec71b41c4d  Nova-Portable-Full-win-x64.zip
71a46a2ff9a80f21356c04bc709730711dc56c044b4de343e66156c115c24efd  nova-bin-win-x64.zip
```
</details>

---

## Core Features

### 1. Overview Dashboard & Live Telemetry
Real-time diagnostic stream, active JVM & heap telemetry, 1-click **Doctor Check / Repair Files**, and automatic pre-launch world snapshots.

<p align="center">
  <img src="docs/screenshots/overview.png" alt="Nova Workbench — Overview Dashboard" width="96%" />
</p>

### 2. Multi-Modloader Matrix (`Vanilla` · `Forge` · `Fabric` · `NeoForge`)
Install any official release (`1.8` – `1.21.4+`) cleanly or pair it in one click with **Forge**, **Fabric**, or **NeoForge**, with automatic Java `8` / `17` / `21` provisioning.

<p align="center">
  <img src="docs/screenshots/versions.png" alt="Nova Workbench — Versions & Modloaders" width="96%" />
</p>

### 3. Dual Mod Catalog, Drag-and-Drop & Bulk SHA-1 Updater
- **Modrinth + CurseForge Catalog:** Browse and install mods filtered automatically to your instance's Minecraft version and modloader, with required dependencies resolved automatically.
- **Bulk SHA-1 Mod Updater:** Hashes 130+ `.jar` (and `.disabled`) files in seconds against Modrinth `/v2/version_files/update` and swaps updates atomically without losing `.disabled` states.
- **Crash Suspect Disabler:** Automatically parses crash reports and adds a **1-click `DISABLE <MOD>`** button right on the crash alert banner.

<p align="center">
  <img src="docs/screenshots/store.png" alt="Nova Workbench — Mod Catalog & Store" width="96%" />
</p>

---

## Portable Directory Architecture

```text
NovaWorkbench/
├── Start Nova.bat          # Quick launcher shortcut
├── settings.json           # Accent theme, CurseForge API key, OTA update channel
├── accounts/               # Saved Microsoft OAuth2 & Offline player profiles
├── bin/                    # Launcher & CLI binaries ONLY (swapped during OTA updates)
│   ├── Nova.exe            # Nova Workbench desktop UI
│   ├── nova-cli.exe        # Headless command-line interface
│   └── Nova.Core.dll       # Shared core engine
├── instances/              # Isolated modpacks & profiles (mods, config, saves)
├── versions/               # Mojang, Forge, Fabric & NeoForge manifests
├── libraries/              # Shared Maven library cache (deduplicated across packs)
├── assets/                 # Shared Minecraft asset object store
├── java/                   # Auto-provisioned portable Adoptium JDK 8 / 17 / 21
├── backups/                # Non-destructive world snapshots (.zip)
└── logs/                   # UI & diagnostic logs
```

---

## Command-Line Interface (`nova-cli`)

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
nova-cli self-update check
nova-cli self-update apply
```

---

## Frequently Asked Questions (FAQ)

<details>
<summary><strong>Will updating Nova Launcher delete my worlds, mods, or accounts?</strong></summary>

**Never.** Nova's OTA updater (`LauncherUpdater`) replaces only the lightweight executables inside `bin\`. Your `instances\`, `saves\`, `mods\`, `accounts\`, and `settings.json` live outside `bin\` and are never touched.
</details>

<details>
<summary><strong>Do I need to install Java or .NET 8 manually?</strong></summary>

**No.** `Nova-Setup-Full.exe` and `Nova-Portable-Full-win-x64.zip` bundle the `.NET 8` runtime inside `bin\`, and Nova automatically downloads the exact portable Java runtime (`JDK 8` for 1.8–1.16.5, `JDK 17` for 1.17–1.20.4, `JDK 21` for 1.20.5+) into `java\`.
</details>

<details>
<summary><strong>How do I enable CurseForge mod search alongside Modrinth?</strong></summary>

Modrinth works out of the box with zero configuration. To unlock CurseForge search and pack mod downloads, paste a free Eternal API key from [console.curseforge.com](https://console.curseforge.com/) into **Configuration → CurseForge API Token**.
</details>

---

## Building & Contributing

See [**CONTRIBUTING.md**](CONTRIBUTING.md) for build instructions and architecture notes, and [**CHANGELOG.md**](CHANGELOG.md) for release history.

## License

Released under the [MIT License](LICENSE).  
*Nova Launcher is an independent open-source project and is not affiliated with or endorsed by Mojang Studios or Microsoft.*
