# Changelog

All notable changes to MyWorkStation will be documented in this file.

## [Unreleased]

### Added

- Added a forced folder synchronization action for active emitters.
- Added synchronized snapshot version visibility in folder sync details.

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
