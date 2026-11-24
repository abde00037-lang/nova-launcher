# Security Policy

## Supported Versions

Only the latest published `v2.x` release receives active security and bugfix updates via Nova's built-in OTA Self-Updater:

| Version | Supported |
| :--- | :---: |
| **`v2.1.x` (Latest)** | **Yes** |
| `< v2.1.0` | Upgrade via **UPDATE & RESTART** |

## Reporting a Vulnerability

If you discover a security vulnerability in **Nova Launcher** (for example: path traversal during modpack import, token storage handling, or OTA update verification):

1. **Do NOT open a public GitHub issue** with exploit details.
2. Report it privately via [**GitHub Private Vulnerability Reporting**](https://github.com/abde00037-lang/nova-launcher/security/advisories/new) on this repository.
3. Include affected version, reproduction steps, and potential impact. We aim to acknowledge reports within 48 hours and ship a patched OTA release promptly.

## Binary Transparency & Checksums

- Every official release binary (`Nova-Setup-Full.exe`, `Nova-Web-Setup.exe`, `Nova-Portable-Full-win-x64.zip`, and `nova-bin-win-x64.zip`) is built directly from public source code on GitHub-hosted runners via [`.github/workflows/release.yml`](.github/workflows/release.yml).
- Every release publishes **`SHA256SUMS.txt`** alongside GitHub's native per-asset SHA-256 digests so users can independently verify file integrity before execution.
- Microsoft account sign-in uses Microsoft's official browser **OAuth2 Device-Code Flow** — Nova never prompts for or sees your Microsoft password.
