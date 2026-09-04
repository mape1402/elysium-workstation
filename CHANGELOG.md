# Changelog

All notable changes to MyWorkStation will be documented in this file.

## [Unreleased]

## [v3.0.0] - 2026-09-04

### Added

- Added the MyWorkStation CLI (`mws`) with a shared Engine contract, Named Pipe control host, default aliases, AI-agent quick context documentation, and initial commands for status, diagnostics, sync control, remote execution, Git, file sending, updates, and workflows.
- Added CLI commands to create folder sync links, send invites, list incoming invites, accept/reject invites, delete links, and run end-to-end dummy sync simulations.
- Added a companion `mws-engine-host.exe` bridge so the CLI talks to an external host process that follows the lifetime of the owning app instance.
- Added UI support to register or remove `mws.exe` from the user PATH.
- Added a visible updater progress window while the external PowerShell updater applies downloaded release files.

## [v2.3.1] - 2026-09-03

### Fixed

- Fixed `.gitignore` folder sync matching for Visual Studio-style bracket patterns, anchored folder patterns, escaped spaces, and ignored directory deletes.

## [v2.3.0] - 2026-09-03

### Added

- Added a forced folder synchronization action for active emitters.
- Added synchronized snapshot version visibility in folder sync details.
- Added a GitHub Releases updater in settings to download and apply the latest Windows build.

### Changed

- App build metadata now resolves from the release tag or `.release` instead of hardcoded project values.
- Folder sync snapshots now use content hashes with origin metadata to avoid sending unchanged received files back after role inversion.

## [v2.2.8] - 2026-09-02

### Fixed

- Removed the Android target from the Windows release build.

## [v2.2.7] - 2026-09-02

### Changed

- Prepared release workflow validation with version 2.2.7.

## [v2.2.6] - 2026-09-02

### Added

- Added GitHub Actions workflows for pull request validation and release publishing from `.release` and `CHANGELOG.md`.

## [v2.2.5] - 2026-09-02

### Added

- Added automatic `.gitignore` support for folder synchronization ignores.

### Fixed

- Fixed remote terminal interrupt flow and busy state handling.
