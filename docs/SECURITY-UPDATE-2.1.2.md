# Nova 2.1.2: security and compatibility notes

- Offline player names must be 3–16 ASCII letters, digits or underscores; duplicate normalized offline account IDs are refused. Previously saved accounts are not deleted.
- Instance IDs used by the instance store cannot contain path separators or dots.
- Microsoft account tokens are protected using Windows DPAPI for the current Windows user. Legacy plaintext tokens are migrated on successful account read on Windows; if writing fails, the original file is left in place. Protected tokens are not portable across Windows users or machines, and older Nova versions cannot read them; sign in again if needed. This does not encrypt other account fields or remove old backups containing plaintext tokens.
- The list is sourced from the Mojang manifest without an artificial 1.21.4 upper limit. Listing a version or loader does not guarantee that every game and loader combination will run. The Java 25 default for 26.x is a fallback; use version metadata where available.
- The new updater requires the release's SHA256SUMS.txt and checks the downloaded OTA archive before extraction. The checksum and archive come from the same GitHub release; this is integrity checking, not an independent signature. A 2.1.0 or 2.1.1 updater cannot perform this new verification on the upgrade to 2.1.2; subsequent updates made by 2.1.2 can. If a release is missing its checksum, 2.1.2 refuses to stage it.
- Download failures include a sanitized diagnostic instead of only a generic failure. This does not establish the cause of earlier download failures.

`tests/Nova.Regression` uses synthetic account tokens and local loopback update files to exercise validation, Windows DPAPI migration/round-trip, matching and mismatched checksums. `scripts/test-ota.ps1` tests overlay of a v2.1.0 portable baseline and verifies sentinel data remains intact. These tests do not replace live Microsoft sign-in, actual gameplay, or exhaustive loader/version testing.
