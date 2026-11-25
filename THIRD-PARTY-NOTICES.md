# Third-Party Notices / إشعارات الطرف الثالث

This file lists third-party components that Nova Launcher ships with or downloads at runtime, and their licenses. Nova's own code and assets are licensed under CC BY-NC 4.0 (see [LICENSE](LICENSE)); the components below keep their own licenses.
يسرد هذا الملف مكونات الطرف الثالث التي تُشحَن مع Nova أو تُنزَّل وقت التشغيل ورخصها. كود Nova وأصوله الخاصة بموجب CC BY-NC 4.0 (انظر [LICENSE](LICENSE))، والمكونات أدناه تحتفظ برخصها.

## Bundled in release packages / مضمّن في حزم الإصدار

### .NET runtime and WPF (Windows Desktop runtime)
- **License:** MIT License
- **Copyright:** (c) .NET Foundation and Contributors
- Full/Portable and OTA packages embed a self-contained .NET 8 runtime (for example `coreclr.dll`, `hostfxr.dll`, `hostpolicy.dll`) and WPF assemblies so Nova runs without a separate .NET install.
- MIT permission notice:

  Permission is hereby granted, free of charge, to any person obtaining a copy
  of this software and associated documentation files (the "Software"), to deal
  in the Software without restriction, including without limitation the rights
  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
  copies of the Software, and to permit persons to whom the Software is
  furnished to do so, subject to the following conditions:

  The above copyright notice and this permission notice shall be included in all
  copies or substantial portions of the Software.

  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
  SOFTWARE.

- The .NET runtime itself contains further third-party components; their notices are maintained upstream: <https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.md> and <https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.md>.

## Downloaded at runtime, not redistributed by Nova / يُنزَّل وقت التشغيل ولا تعيد Nova توزيعه

### Adoptium Eclipse Temurin JDK
- **License:** GNU General Public License, version 2, with the Classpath Exception (GPL-2.0 WITH Classpath-exception-2.0)
- Downloaded from the official Adoptium API (<https://api.adoptium.net/>) into the launcher's `versions/` area when a Minecraft version needs Java. It runs as a separate program; Nova does not link against it or bundle it in release packages.

### Minecraft game, libraries and assets
- **License/terms:** Proprietary; governed by the [Minecraft EULA](https://www.minecraft.net/eula) and Mojang/Microsoft terms.
- Version manifests and libraries are fetched from official Mojang endpoints (`piston-meta.mojang.com`, `launchermeta.mojang.com`, `libraries.minecraft.net`) at the user's request. Nova does not redistribute Minecraft content.

### Minecraft Forge
- **License:** GNU Lesser General Public License v2.1 (LGPL-2.1)
- Installer and loader files are downloaded from the official Forge maven (`maven.minecraftforge.net`, `files.minecraftforge.net`) and run as separate programs. Nova does not link against Forge code or redistribute it.

### Fabric / NeoForge and other loaders
- Downloaded from their official sources when the user selects them; each keeps its own license (for example Fabric loader is Apache-2.0). Separate programs, not redistributed by Nova.

### Mods and modpacks (Modrinth, CurseForge)
- **License:** Each mod/modpack keeps the license chosen by its author (for example MIT, GPL, LGPL, ARR). Metadata and files are fetched from Modrinth (<https://api.modrinth.com>) and CurseForge (<https://api.curseforge.com>, cfwidget) at the user's request. Nova never relicenses mod content; check each mod page for its terms.

### Microsoft / Xbox Live authentication
- Account sign-in uses Microsoft's official OAuth2 device-code flow and Minecraft services APIs. No Microsoft code is bundled; use is governed by Microsoft's terms of service.

## Build and CI tooling / أدوات البناء وCI (not shipped / لا تُشحَن)

- GitHub Actions `actions/checkout`, `actions/setup-dotnet`, `softprops/action-gh-release`: MIT License. Executed on GitHub-hosted runners during CI/release builds only; not part of shipped packages.
- .NET 8 SDK and Windows build images: used to compile Nova; their licenses apply to the tooling, not to Nova's output beyond the bundled runtime noted above.

## Nova assets / أصول Nova

- Logo, icons, screenshots and documentation images in this repository are Nova's own material under CC BY-NC 4.0. UI screenshots may depict references to Minecraft versions and profiles; all trademarks belong to their owners.
