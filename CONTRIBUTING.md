# Contributing to Nova Launcher

Thank you for your interest in contributing to **Nova Launcher**!

## Project Structure

| Project | Target | Description |
| :--- | :--- | :--- |
| `src/Nova.Core` | `net8.0` | Shared launcher core: Mojang/Forge/Fabric/NeoForge installers, launch planner, SHA-1 downloader, mod updater, pack importer/exporter, crash diagnostics, and OTA self-updater. |
| `src/Nova.App` | `net8.0-windows` | WPF desktop interface (*Nova Workbench*). |
| `src/Nova.Cli` | `net8.0` | Headless command-line interface (`nova-cli`). |
| `installer/` | `.NET Framework 4.x` | Zero-dependency Windows installers (`NovaOfflineSetup.cs` & `NovaWebSetup.cs`). |

## Local Development

1. Install the [**.NET 8.0 SDK**](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows (`win-x64`).
2. Clone the repository:
   ```powershell
   git clone https://github.com/abde00037-lang/nova-launcher.git
   cd nova-launcher
   ```
3. Build both executables:
   ```powershell
   dotnet build src/Nova.App/Nova.csproj -c Release -r win-x64 --no-self-contained
   dotnet build src/Nova.Cli/nova-cli.csproj -c Release -r win-x64 --no-self-contained
   ```

## Pull Request Guidelines

- Keep `Nova.Core` platform-clean and free of UI dependencies so every feature works identically in both `Nova.App` and `nova-cli`.
- Never write files outside the portable launcher root (`LauncherPaths.Root`) or delete user saves/mods destructively.
- Ensure CI (`CI Build & Verify`) passes with `0` errors before requesting a review.
