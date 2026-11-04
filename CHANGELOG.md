# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.0.0] - 2026-11-03

First public release.

### Added
- 16-tile main window: files, status, terminal, reboot/shutdown, network info, update packages, services, run command, processes, Docker, logs, port forwarding, system config, hardware info, storage/NVMe, backup.
- System detection after connecting: tiles that do not work on the board's system are greyed out with a reason.
- The `opi` command for cmd / PowerShell (opens a shell on the board, like `wsl`).
- Light and dark theme.
- Languages: English, فارسی (Persian), 中文 (Chinese), Русский (Russian), हिन्दी (Hindi). Pick one (with its flag) in the top-right corner; the app restarts to apply it. Texts live in `lang/*.txt`; anything not translated stays English.
- Confirmation dialogs for every action that changes the board.
- Version info embedded in `OpiLink.exe`.
- Repository files: README, LICENSE (MIT), CI and release workflows, community files.

### Changed
- `README.txt` replaced by `README.md`; planning documents moved to `docs/`.

[Unreleased]: https://github.com/OrangePiShell/Opilink/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/OrangePiShell/Opilink/releases/tag/v1.0.0
