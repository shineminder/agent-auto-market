# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions: [SemVer](https://semver.org/).

## [0.1.0] - 2026-10-02

### Added
- Windows service and Linux systemd service, pairing by code, Coinbase key encrypted locally.
- Single-use authorization, confirmation wait, idempotent reporting.
- Local limits (per order, per month, assets).
- Signed updates, controlled globally or per machine, no automatic downgrade.
- Site address change without pairing again (`cc-agent server --url`).
- Windows and Linux installers, cloud-init.
