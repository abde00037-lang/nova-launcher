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

- The four v2.1.0 binary assets were built and uploaded by [GitHub Actions run #3](https://github.com/abde00037-lang/nova-launcher/actions/runs/45438579706). The current [release workflow](.github/workflows/release.yml) is configured to build release packages on GitHub-hosted runners.
- For v2.1.0, **`SHA256SUMS.txt`** was uploaded separately after the build; the current workflow is configured to generate it for future releases. GitHub also displays per-asset SHA-256 digests. Hashes help verify that a download matches the published file; they do not prove it is safe.
- Microsoft account sign-in uses Microsoft's official browser **OAuth2 Device-Code Flow** — Nova never prompts for or sees your Microsoft password.
