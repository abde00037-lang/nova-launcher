# Security Policy

## Supported Versions

Only the latest published `v2.x` release receives active security and bugfix updates via Nova's built-in OTA Self-Updater:

| Version | Supported |
| :--- | :---: |
| [Latest published release](https://github.com/abde00037-lang/nova-launcher/releases/latest) | Yes |
| Older releases | Upgrade to the latest release; back up data first |

## Reporting a Vulnerability

If you discover a security vulnerability in **Nova Launcher** (for example: path traversal during modpack import, token storage handling, or OTA update verification):

1. **Do NOT open a public GitHub issue** with exploit details.
2. Report it privately via [**GitHub Private Vulnerability Reporting**](https://github.com/abde00037-lang/nova-launcher/security/advisories/new) on this repository.
3. Include affected version, reproduction steps, and potential impact. We aim to acknowledge reports within 48 hours and ship a patched OTA release promptly.

## Binary Transparency & Checksums

- The four v2.1.0 binary assets were built and uploaded by [GitHub Actions run #3](https://github.com/abde00037-lang/nova-launcher/actions/runs/45438579706). The current [release workflow](.github/workflows/release.yml) is configured to build release packages on GitHub-hosted runners.
- For v2.1.0, **`SHA256SUMS.txt`** was uploaded separately after the build; the current workflow is configured to generate it for future releases. GitHub also displays per-asset SHA-256 digests. Hashes help verify that a download matches the published file; they do not prove it is safe.
- Microsoft account sign-in uses Microsoft's official browser **OAuth2 Device-Code Flow** — Nova never prompts for or sees your Microsoft password.

## Archive extraction limits

The source has configurable limits for untrusted archives. Defaults are 100,000 entries, 2,147,483,648 B per entry, 17,179,869,184 B total, and 33,554,432 B for a pack manifest. Set positive integer environment variables `NOVA_ARCHIVE_MAX_ENTRIES`, `NOVA_ARCHIVE_MAX_FILE_BYTES`, `NOVA_ARCHIVE_MAX_TOTAL_BYTES`, or `NOVA_ARCHIVE_MAX_MANIFEST_BYTES` before starting Nova to adjust them. API callers can pass an `ArchiveLimits` instance to native extraction or pack import. Limits apply before and during extraction; existing filesystem links are rejected in extraction paths. A malicious local process actively racing filesystem operations is outside this protection; use OS permissions and an isolated data directory.

## Updater recovery limits

Update downloads use HTTPS and SHA-256 checks against the release checksum file. A checksum from the same release is not an independent signature. The updater backs up replaced files and attempts to roll back ordinary copy failures; sudden power loss, forced termination, filesystem races, or a failed rollback can still leave a partial installation. Keep independent backups of launcher data before updating. The downloader needs a server supporting byte ranges to resume; otherwise it safely restarts the download.
