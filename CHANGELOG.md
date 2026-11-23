# Changelog

All notable changes to this package will be documented in this file.

## [1.0.1] - 2026-11-23

### Fixed

- Ship a `.meta` file for every asset. Packages installed from a Git URL live in a
  read-only `PackageCache`, so Unity cannot generate `.meta` files itself and
  silently ignores every asset without one
  ("has no meta file, but it's in an immutable folder. The asset will be ignored").
## [1.0.0] - 2026-11-23

### Added

- Project window context menu entry `变体转换本体（Variant to Prefab）`.
- `转换选中变体（Convert Selected）`: converts every selected Prefab Variant in place,
  preserving the asset GUID. Folders are processed recursively, regular prefabs are skipped.
- `扫描变体（Scan Variants）`: lists all Prefab Variants under the selected folder without
  modifying anything.
- Root-only unpack: nested prefabs inside the hierarchy stay nested (unlike
  `Unpack Prefab Completely`).
- Post-conversion verification: checks the file on disk for a leftover root-level
  prefab instance and warns if the asset GUID changed.
