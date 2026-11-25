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

## Regression Tests

Run the regression suites on Windows with .NET 8:

```powershell
dotnet run --project tests/Nova.Regression/Nova.Regression.csproj -c Release
dotnet run --project tests/Nova.GuiRegression/Nova.GuiRegression.csproj -c Release
```

The WPF suite exercises profile switching and page layout, including inner scrolling and resizing. The interactive window test also checks fullscreen, maximizing, and small-window scrolling; it opens a window and needs an interactive Windows desktop. Point it at a disposable build, **not** an existing user installation:

```powershell
./tests/test-window-layout.ps1 -Exe "<absolute path to disposable build>/Nova.exe"
```

The test creates temporary fixture data. CI and release workflows also verify the update overlay and installers. Keep independent backups of any real data before manual testing.

## Pull Request Guidelines

- Keep `Nova.Core` platform-clean and free of UI dependencies so every feature works identically in both `Nova.App` and `nova-cli`.
- Never write files outside the portable launcher root (`LauncherPaths.Root`) or delete user saves/mods destructively.
- Ensure CI (`CI Build & Verify`) passes with `0` errors before requesting a review.
